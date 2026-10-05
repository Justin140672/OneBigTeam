using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InterviewOutcomeTaskReconciliationService(
    RecruitmentDbContext db,
    ITaskResolution taskResolution,
    InterviewOutcomeAuditDelivery auditDelivery,
    IClock clock,
    ILogger<InterviewOutcomeTaskReconciliationService> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    private const int BatchSize = 100;

    public async Task<int> RunOutstandingForInterviewAsync(
        Guid companyId, Guid interviewId, CancellationToken cancellationToken)
    {
        var outstanding = await db.InterviewOutcomeTaskReconciliations
            .Where(r => r.CompanyId == companyId && r.InterviewId == interviewId
                && r.CompletedAt == null && r.BlockedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var record in outstanding)
            await RunAsync(record, cancellationToken);

        return outstanding.Count;
    }

    public async Task<int> RunAllOutstandingAsync(CancellationToken cancellationToken)
    {
        var outstanding = await db.InterviewOutcomeTaskReconciliations
            .Where(r => r.CompletedAt == null && r.BlockedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var record in outstanding)
            await RunAsync(record, cancellationToken);

        return outstanding.Count;
    }

    public async Task<bool> RunAsync(InterviewOutcomeTaskReconciliation record, CancellationToken cancellationToken)
    {
        if (!record.TryClaim(clock.UtcNowOffset()))
            return false;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(record).State = EntityState.Detached;
            return false;
        }

        try
        {
            Exception? auditFailure = null;
            InterviewOutcomeSourceDataMissingException? sourceDataMissing = null;
            try
            {
                await auditDelivery.DeliverAsync(record, cancellationToken);
            }
            catch (InterviewOutcomeSourceDataMissingException ex)
            {
                sourceDataMissing = ex;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                auditFailure = ex;
            }

            var completion = await taskResolution.CompleteBySourceEntityConfirmedAsync(
                record.CompanyId, record.InterviewId, TaskSource.Recruitment, TaskActionType.Complete,
                record.RecordedBy, cancellationToken,
                TaskCompletionDispatchMode.BusinessEffectAlreadyApplied);
            var cancelled = await taskResolution.CancelBySourceEntitiesConfirmedAsync(
                record.CompanyId, [record.InterviewId], TaskSource.Recruitment, TaskActionType.Review, cancellationToken);

            if (completion.Status == TaskResolutionStatus.TerminalFailure)
            {
                await BlockAsync(
                    record,
                    InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure,
                    completion.FailureReason ?? "The Tasks completion failed permanently.",
                    completion.TaskId,
                    completion.OperationId,
                    completion.TerminalFailureAt,
                    cancellationToken);
                return false;
            }

            if (sourceDataMissing is not null)
            {
                await BlockAsync(
                    record,
                    InterviewOutcomeTaskReconciliation.BlockedAuditSourceDataMissing,
                    sourceDataMissing.Message,
                    completion.TaskId,
                    completion.OperationId,
                    null,
                    cancellationToken);
                return false;
            }

            if (auditFailure is not null)
                throw auditFailure;

            if ((completion.Status is not (TaskResolutionStatus.Confirmed or TaskResolutionStatus.Waived)) || !cancelled)
                throw new InvalidOperationException("Task effects are not yet confirmed.");

            if (completion.Status == TaskResolutionStatus.Waived)
            {
                record.MarkCompletedWaived(completion.OperationId ?? Guid.Empty, clock.UtcNowOffset());
                logger.LogWarning(
                    "Interview outcome reconciliation closed because its Tasks completion was operator-waived; the Tasks effects were not confirmed. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} TasksOperationId={TasksOperationId}",
                    record.Id, record.CompanyId, record.InterviewId, completion.OperationId);
            }
            else
            {
                record.MarkCompleted(clock.UtcNowOffset());
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Interview outcome task reconciliation is unconfirmed and will be retried. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId} AttemptCount={AttemptCount} Blocked={Blocked} FailureCategory={FailureCategory} CorrelationId={CorrelationId}",
                record.Id, record.CompanyId, record.InterviewId, record.ApplicationId,
                record.AttemptCount + 1, false, ex.GetType().Name, executionContextAccessor?.Current?.CorrelationId);

            try
            {
                record.MarkFailed(ex.Message, clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to record interview outcome task reconciliation failure {ReconciliationId}.", record.Id);
            }

            return false;
        }
    }

    private async Task BlockAsync(
        InterviewOutcomeTaskReconciliation record,
        string category,
        string reason,
        Guid? taskId,
        Guid? tasksOperationId,
        DateTimeOffset? tasksTerminalFailureAt,
        CancellationToken cancellationToken)
    {
        record.Block(category, reason, taskId, tasksOperationId, clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);

        logger.LogError(
            "Interview outcome task reconciliation is blocked and requires operator attention; it is excluded from ordinary sweeps until repaired. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId} TaskId={TaskId} TasksOperationId={TasksOperationId} AttemptCount={AttemptCount} Blocked={Blocked} FailureCategory={FailureCategory} TasksTerminalFailureAt={TasksTerminalFailureAt} CorrelationId={CorrelationId}",
            record.Id, record.CompanyId, record.InterviewId, record.ApplicationId, taskId, tasksOperationId,
            record.AttemptCount, true, category, tasksTerminalFailureAt, executionContextAccessor?.Current?.CorrelationId);
    }
}
