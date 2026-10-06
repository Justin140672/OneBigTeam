using System.Security.Cryptography;
using System.Text;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InternalOfferTaskEffectsService(
    RecruitmentDbContext db,
    ITaskCreator taskCreator,
    ITaskCanceller taskCanceller,
    ITaskResolution taskResolution,
    INotificationWriter notificationWriter,
    IClock clock,
    ILogger<InternalOfferTaskEffectsService> logger)
{
    private const int BatchSize = 100;

    private enum Closure
    {
        Complete,
        Cancel,
    }

    private sealed record ApplicationState(
        Guid VacancyId,
        Guid CandidateId,
        int OfferVersion,
        OfferResponseStatus? Status,
        DateTimeOffset? WithdrawnAt,
        bool IsTerminalStage,
        Guid? RespondedByUserId,
        OfferResponseChannel? Channel,
        Guid? OfferMadeByUserId);

    public static string TaskIdempotencyKey(Guid applicationId, int offerVersion) =>
        $"recruitment-internal-offer:{applicationId}:{offerVersion}";

    public static string TaskTitle(string jobTitle) => $"Review your internal job offer — {jobTitle}";

    public static Guid NotificationSourceId(Guid applicationId, int offerVersion)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"internal-offer-response:{applicationId}:{offerVersion}"));
        return new Guid(hash);
    }

    public async Task<int> RunOutstandingForApplicationAsync(
        Guid companyId, Guid applicationId, CancellationToken cancellationToken)
    {
        var outstanding = await db.InternalOfferTaskEffects
            .Where(e => e.CompanyId == companyId && e.ApplicationId == applicationId && e.ClosedAt == null)
            .OrderBy(e => e.OfferVersion)
            .ToListAsync(cancellationToken);

        foreach (var effect in outstanding)
            await RunAsync(effect, cancellationToken);

        return outstanding.Count;
    }

    public async Task<int> RunAllOutstandingAsync(CancellationToken cancellationToken)
    {
        var outstandingIds = await (
            from e in db.InternalOfferTaskEffects.AsNoTracking()
            join a in db.Applications.AsNoTracking() on e.ApplicationId equals a.Id
            join s in db.RecruitmentStages.AsNoTracking() on a.CurrentStageId equals s.Id
            where e.ClosedAt == null
               && (e.TaskCreatedAt == null
                   || a.OfferVersion != e.OfferVersion
                   || a.OfferResponseStatus != OfferResponseStatus.AwaitingResponse
                   || a.WithdrawnAt != null
                   || s.IsTerminal)
            orderby e.LastAttemptAt ?? DateTimeOffset.MinValue, e.OfferVersion
            select e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var outstanding = await db.InternalOfferTaskEffects
            .Where(e => outstandingIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        foreach (var effect in outstanding.OrderBy(e => e.ApplicationId).ThenBy(e => e.OfferVersion))
            await RunAsync(effect, cancellationToken);

        return outstanding.Count;
    }

    public async Task RunAsync(InternalOfferTaskEffect effect, CancellationToken cancellationToken)
    {
        try
        {
            var hasOpenEarlierVersion = await db.InternalOfferTaskEffects
                .AsNoTracking()
                .AnyAsync(e => e.ApplicationId == effect.ApplicationId
                            && e.OfferVersion < effect.OfferVersion
                            && e.ClosedAt == null, cancellationToken);

            if (hasOpenEarlierVersion)
                return;

            var state = await ReadStateAsync(effect, cancellationToken);
            var closure = ResolveClosure(effect, state);

            if (closure is null)
            {
                if (effect.TaskCreatedAt is null)
                {
                    await CreateTaskAsync(effect, cancellationToken);
                    effect.MarkTaskCreated(clock.UtcNowOffset());
                    await db.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            if (closure == Closure.Complete)
            {
                var resolution = await taskResolution.CompleteBySourceEntityConfirmedAsync(
                    effect.CompanyId,
                    effect.ApplicationId,
                    TaskSource.Recruitment,
                    TaskActionType.Approve,
                    state?.RespondedByUserId ?? effect.EmployeeId,
                    cancellationToken,
                    TaskCompletionDispatchMode.BusinessEffectAlreadyApplied);

                if (!resolution.IsConfirmed)
                {
                    effect.MarkFailed($"Task completion is {resolution.Status}.", clock.UtcNowOffset());
                    await db.SaveChangesAsync(cancellationToken);
                    return;
                }

                if (state is { Channel: OfferResponseChannel.Employee } && effect.ResponseNotifiedAt is null)
                {
                    await NotifyRecruiterAsync(effect, state, cancellationToken);
                    effect.MarkResponseNotified(clock.UtcNowOffset());
                }
            }
            else
            {
                await taskCanceller.CancelAllBySourceEntityAsync(
                    effect.CompanyId, effect.ApplicationId, TaskSource.Recruitment, TaskActionType.Approve, cancellationToken);
            }

            effect.MarkClosed(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Internal offer task effects {EffectId} for application {ApplicationId} version {OfferVersion} failed and will be retried.",
                effect.Id, effect.ApplicationId, effect.OfferVersion);

            try
            {
                effect.MarkFailed(ex.Message, clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to record internal offer task effect failure {EffectId}.", effect.Id);
            }
        }
    }

    private static Closure? ResolveClosure(InternalOfferTaskEffect effect, ApplicationState? state)
    {
        if (state is null || state.OfferVersion > effect.OfferVersion)
            return Closure.Cancel;

        if (state.OfferVersion < effect.OfferVersion)
            return null;

        return state.Status switch
        {
            OfferResponseStatus.Accepted or OfferResponseStatus.Declined => Closure.Complete,
            OfferResponseStatus.Withdrawn or null => Closure.Cancel,
            _ => state.WithdrawnAt is not null || state.IsTerminalStage ? Closure.Cancel : null,
        };
    }

    private Task<ApplicationState?> ReadStateAsync(InternalOfferTaskEffect effect, CancellationToken cancellationToken) =>
        (from a in db.Applications.AsNoTracking()
         join s in db.RecruitmentStages.AsNoTracking() on a.CurrentStageId equals s.Id
         where a.Id == effect.ApplicationId && a.CompanyId == effect.CompanyId
         select new ApplicationState(
             a.VacancyId,
             a.CandidateId,
             a.OfferVersion,
             a.OfferResponseStatus,
             a.WithdrawnAt,
             s.IsTerminal,
             a.OfferRespondedByUserId,
             a.OfferResponseChannel,
             a.OfferMadeByUserId))
        .SingleOrDefaultAsync(cancellationToken);

    private async Task CreateTaskAsync(InternalOfferTaskEffect effect, CancellationToken cancellationToken)
    {
        await taskCreator.CreateAsync(
            effect.CompanyId,
            createdBy:          effect.MadeByUserId,
            title:              TaskTitle(effect.JobTitle),
            description:        "You have received an internal job offer. Open the offer to review the full terms, then accept or decline it.",
            priority:           TaskPriority.High,
            source:             TaskSource.Recruitment,
            actionType:         TaskActionType.Approve,
            dueDate:            effect.ResponseDeadline,
            assignedEmployeeId: effect.EmployeeId,
            assignedUserId:     effect.EmployeeId,
            sourceEntityId:     effect.ApplicationId,
            cancellationToken,
            notifyAssignee:     true,
            idempotencyKey:     TaskIdempotencyKey(effect.ApplicationId, effect.OfferVersion));
    }

    private async Task NotifyRecruiterAsync(
        InternalOfferTaskEffect effect, ApplicationState state, CancellationToken cancellationToken)
    {
        var hiringManagerId = await db.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == state.VacancyId && v.CompanyId == effect.CompanyId)
            .Select(v => (Guid?)v.HiringManagerId)
            .SingleOrDefaultAsync(cancellationToken);

        var candidateName = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == state.CandidateId && c.CompanyId == effect.CompanyId)
            .Select(c => c.FirstName + " " + c.LastName)
            .SingleOrDefaultAsync(cancellationToken) ?? "The employee";

        var outcome = state.Status == OfferResponseStatus.Accepted ? "accepted" : "declined";
        var recipients = new[] { hiringManagerId, state.OfferMadeByUserId, effect.MadeByUserId }
            .Where(id => id is not null && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var sourceId = NotificationSourceId(effect.ApplicationId, effect.OfferVersion);
        var now = clock.UtcNowOffset();
        var actionUrl = $"/companies/{effect.CompanyId}/vacancies/{state.VacancyId}/view";

        foreach (var recipient in recipients)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                effect.CompanyId,
                recipient,
                $"Internal offer {outcome}",
                $"{candidateName} has {outcome} the internal offer for {effect.JobTitle}.",
                sourceId,
                NotificationType.InternalOfferResponded,
                NotificationPriority.Normal,
                now,
                cancellationToken,
                actionUrl);
        }
    }
}
