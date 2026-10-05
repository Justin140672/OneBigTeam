using HR.Modules.Tasks.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

// Depends on InterviewOutcomeRecorder (not RecordInterviewOutcomeHandler) deliberately:
// RecordInterviewOutcomeHandler depends on ITaskCompleter, whose implementation graph
// (TaskCompleter -> TaskCompletionDispatcher -> InterviewFeedbackTaskCompletionAction ->
// IInterviewFeedbackService) would loop back here and create a DI constructor cycle.
// InterviewOutcomeRecorder contains the same recording logic without that dependency, and
// this service never needs the completion side-effect anyway: it is only invoked from
// InterviewFeedbackTaskCompletionAction, which runs after TaskCompleter has already marked
// the originating task Completed.
internal sealed class InterviewFeedbackService(
    RecruitmentDbContext db,
    InterviewOutcomeRecorder recorder,
    InterviewOutcomeAuditDelivery auditDelivery,
    IClock clock,
    ILogger<InterviewFeedbackService>? logger = null) : IInterviewFeedbackService
{
    public async Task<Result> RecordFeedbackAsync(
        Guid companyId,
        Guid interviewId,
        Guid recordedByEmployeeId,
        string outcome,
        string? notes,
        CancellationToken cancellationToken,
        Guid dispatchOperationId = default)
    {
        if (!Enum.TryParse<InterviewOutcome>(outcome, ignoreCase: true, out var parsedOutcome))
            return Result.Failure(Error.Validation($"'{outcome}' is not a recognised interview outcome."));

        var location = await (
            from i in db.Interviews.AsNoTracking()
            join a in db.Applications.AsNoTracking() on i.ApplicationId equals a.Id
            where i.Id == interviewId && i.CompanyId == companyId
            select new { a.VacancyId, ApplicationId = a.Id, a.CandidateId, i.Outcome, i.Notes })
            .SingleOrDefaultAsync(cancellationToken);

        if (location is null)
            return Result.Failure(Error.NotFound($"Interview '{interviewId}' was not found."));

        // Ticket 15 (P1): "already recorded with this exact outcome" proves only that a prior
        // dispatch's primary mutation committed — it says nothing about whether its audit event
        // ever published. Only recognised for a genuine Tasks-dispatch replay (a stable
        // dispatchOperationId — ticket 11); a bare re-request with no dispatch identity keeps the
        // original strict behaviour (InterviewOutcomeRecorder rejects it), matching every other
        // ITaskCompletionAction's identical DispatchOperationId-gated recovery guard.
        if (location.Outcome == parsedOutcome
            && parsedOutcome != InterviewOutcome.Pending
            && dispatchOperationId != Guid.Empty)
        {
            return await ConfirmReplayAuditAsync(
                companyId, interviewId, location.ApplicationId, recordedByEmployeeId, cancellationToken);
        }

        var result = await recorder.RecordAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = location.VacancyId,
                ApplicationId = location.ApplicationId,
                InterviewId   = interviewId,
                Outcome       = parsedOutcome,
                Notes         = notes,
            },
            recordedByEmployeeId,
            cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    // Replay of an already-recorded outcome proves only that the primary mutation committed. Audit
    // delivery is driven through the durable reconciliation row (created here for legacy data that
    // lacks one) and is only reported confirmed once the audit event is seen to exist. If it cannot
    // be confirmed yet, the outstanding row is the durable handoff: reconciliation delivers it.
    private async Task<Result> ConfirmReplayAuditAsync(
        Guid companyId, Guid interviewId, Guid applicationId, Guid recordedBy, CancellationToken cancellationToken)
    {
        var record = await FindReconciliationAsync(companyId, interviewId, cancellationToken);

        if (record is null)
        {
            record = InterviewOutcomeTaskReconciliation.Create(
                Guid.NewGuid(), companyId, applicationId, interviewId, recordedBy, clock.UtcNowOffset());
            db.InterviewOutcomeTaskReconciliations.Add(record);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger?.LogWarning(
                    "Created a repair interview-outcome reconciliation for an outcome recorded without one. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId}",
                    record.Id, companyId, interviewId, applicationId);
            }
            catch (DbUpdateException)
            {
                db.Entry(record).State = EntityState.Detached;
                record = await FindReconciliationAsync(companyId, interviewId, cancellationToken);

                if (record is null)
                    throw;
            }
        }

        if (record.IsBlocked)
        {
            return Result.Failure(Error.Conflict(
                $"Interview outcome reconciliation {record.Id} for interview {interviewId} requires operator attention ({record.BlockedCategory}) before its audit can be confirmed."));
        }

        if (record.AuditDeliveredAt is not null)
            return Result.Success();

        try
        {
            await auditDelivery.DeliverAsync(record, cancellationToken);
        }
        catch (InterviewOutcomeSourceDataMissingException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex,
                "Interview outcome audit is unconfirmed on replay; the outstanding reconciliation is the durable handoff. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId}",
                record.Id, companyId, interviewId, applicationId);
        }

        return Result.Success();
    }

    private Task<InterviewOutcomeTaskReconciliation?> FindReconciliationAsync(
        Guid companyId, Guid interviewId, CancellationToken cancellationToken) =>
        db.InterviewOutcomeTaskReconciliations
            .SingleOrDefaultAsync(r => r.CompanyId == companyId && r.InterviewId == interviewId, cancellationToken);
}
