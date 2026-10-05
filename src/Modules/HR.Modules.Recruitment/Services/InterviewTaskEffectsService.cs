using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InterviewTaskEffectsService(
    RecruitmentDbContext db,
    ITaskCreator taskCreator,
    ITaskCanceller taskCanceller,
    ITaskCompleter taskCompleter,
    IClock clock,
    ILogger<InterviewTaskEffectsService> logger)
{
    private const int BatchSize = 100;

    public static string TaskIdempotencyKey(Guid interviewId, TaskActionType actionType) =>
        $"recruitment-interview:{interviewId}:{actionType}";

    public async Task<int> RunOutstandingForApplicationAsync(
        Guid companyId, Guid applicationId, CancellationToken cancellationToken)
    {
        var outstanding = await db.InterviewTaskEffects
            .Where(e => e.CompanyId == companyId && e.ApplicationId == applicationId && e.CompletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var effect in outstanding)
            await RunAsync(effect, cancellationToken);

        return outstanding.Count;
    }

    public async Task<int> RunAllOutstandingAsync(CancellationToken cancellationToken)
    {
        var outstanding = await db.InterviewTaskEffects
            .Where(e => e.CompletedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var effect in outstanding)
            await RunAsync(effect, cancellationToken);

        return outstanding.Count;
    }

    public async Task<bool> RunAsync(InterviewTaskEffect effect, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await ReadOutcomeAsync(effect, cancellationToken);

            if (outcome == InterviewOutcome.Pending)
            {
                await CreateTasksAsync(effect, cancellationToken);
                outcome = await ReadOutcomeAsync(effect, cancellationToken);
            }

            if (outcome == InterviewOutcome.Cancelled)
            {
                IReadOnlyCollection<Guid> ids = [effect.InterviewId];
                await taskCanceller.CancelManyBySourceEntitiesAsync(
                    effect.CompanyId, ids, TaskSource.Recruitment, TaskActionType.Review, cancellationToken);
                await taskCanceller.CancelManyBySourceEntitiesAsync(
                    effect.CompanyId, ids, TaskSource.Recruitment, TaskActionType.Complete, cancellationToken);
            }
            else if (outcome is not null and not InterviewOutcome.Pending)
            {
                await taskCanceller.CancelManyBySourceEntitiesAsync(
                    effect.CompanyId, [effect.InterviewId], TaskSource.Recruitment, TaskActionType.Review, cancellationToken);
                await taskCompleter.CompleteBySourceEntityAsync(
                    effect.CompanyId, effect.InterviewId, TaskSource.Recruitment, TaskActionType.Complete,
                    effect.InterviewerEmployeeId, cancellationToken);
            }

            effect.MarkCompleted(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
            return outcome != InterviewOutcome.Cancelled;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Interview task effects {EffectId} for interview {InterviewId} failed and will be retried.",
                effect.Id, effect.InterviewId);

            try
            {
                effect.MarkFailed(ex.Message, clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to record interview task effects failure {EffectId}.", effect.Id);
            }

            return true;
        }
    }

    private Task<InterviewOutcome?> ReadOutcomeAsync(InterviewTaskEffect effect, CancellationToken cancellationToken) =>
        db.Interviews
            .AsNoTracking()
            .Where(i => i.Id == effect.InterviewId && i.CompanyId == effect.CompanyId)
            .Select(i => (InterviewOutcome?)i.Outcome)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task CreateTasksAsync(InterviewTaskEffect effect, CancellationToken cancellationToken)
    {
        var interviewDate = DateOnly.FromDateTime(effect.ScheduledAt.UtcDateTime);

        await taskCreator.CreateAsync(
            effect.CompanyId,
            createdBy:          effect.ScheduledBy,
            title:              $"Prepare for interview: {effect.CandidateName}",
            description:        $"Review {effect.CandidateName}'s application for the {effect.VacancyTitle} vacancy ahead of the interview.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Recruitment,
            actionType:         TaskActionType.Review,
            dueDate:            interviewDate,
            assignedEmployeeId: effect.InterviewerEmployeeId,
            assignedUserId:     effect.InterviewerEmployeeId,
            sourceEntityId:     effect.InterviewId,
            cancellationToken,
            notifyAssignee:     false,
            idempotencyKey:     TaskIdempotencyKey(effect.InterviewId, TaskActionType.Review));

        await taskCreator.CreateAsync(
            effect.CompanyId,
            createdBy:          effect.ScheduledBy,
            title:              $"Provide feedback: interview with {effect.CandidateName}",
            description:        $"Record the outcome and feedback for {effect.CandidateName}'s interview for the {effect.VacancyTitle} vacancy.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Recruitment,
            actionType:         TaskActionType.Complete,
            dueDate:            interviewDate.AddDays(1),
            assignedEmployeeId: effect.InterviewerEmployeeId,
            assignedUserId:     effect.InterviewerEmployeeId,
            sourceEntityId:     effect.InterviewId,
            cancellationToken,
            notifyAssignee:     false,
            idempotencyKey:     TaskIdempotencyKey(effect.InterviewId, TaskActionType.Complete));
    }
}
