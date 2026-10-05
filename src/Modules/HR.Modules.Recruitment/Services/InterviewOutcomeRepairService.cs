using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InterviewOutcomeRepairService(
    RecruitmentDbContext db,
    ITaskCompletionOperationStateReader tasksStateReader,
    InterviewOutcomeRepairAuditDelivery auditDelivery,
    IClock clock,
    ILogger<InterviewOutcomeRepairService> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public const string TasksResetReason = "The linked Tasks completion was reset by an operator.";
    public const string TasksAdjudicationReason = "The linked Tasks completion was adjudicated by an operator.";

    private const int BatchSize = 100;

    /// <summary>
    /// Unblocks the reconciliation and saves its durable audit intent in one transaction. Returns the
    /// action, or null when a concurrent writer repaired the record first (nothing is saved).
    /// </summary>
    public async Task<InterviewOutcomeRepairAction?> UnblockAsync(
        InterviewOutcomeTaskReconciliation record,
        Guid operatorUserId,
        string reason,
        string source,
        CancellationToken cancellationToken)
    {
        var category = record.BlockedCategory ?? string.Empty;
        var tasksOperationId = record.BlockedTasksOperationId;
        var now = clock.UtcNowOffset();

        record.Unblock(operatorUserId, now);

        var correlation = executionContextAccessor?.Current is { } context
            ? CorrelationIdGuid.Derive(context.CorrelationId)
            : (Guid?)null;

        var action = InterviewOutcomeRepairAction.Create(
            record, category, tasksOperationId, operatorUserId, reason, source, now, correlation);

        if (await db.InterviewOutcomeRepairActions.AsNoTracking().AnyAsync(a => a.Id == action.Id, cancellationToken))
        {
            db.Entry(record).State = EntityState.Detached;
            return null;
        }

        db.InterviewOutcomeRepairActions.Add(action);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(action).State = EntityState.Detached;
            db.Entry(record).State = EntityState.Detached;
            return null;
        }
        catch (DbUpdateException)
        {
            db.Entry(action).State = EntityState.Detached;
            db.Entry(record).State = EntityState.Detached;

            if (await db.InterviewOutcomeRepairActions.AsNoTracking().AnyAsync(a => a.Id == action.Id, CancellationToken.None))
                return null;

            throw;
        }

        logger.LogWarning(
            "Blocked interview outcome reconciliation unblocked. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId} TasksOperationId={TasksOperationId} RecoveryActionId={RecoveryActionId} OperatorUserId={OperatorUserId} RepairCount={RepairCount} FailureCategory={FailureCategory} Source={Source} CorrelationId={CorrelationId}",
            record.Id, record.CompanyId, record.InterviewId, record.ApplicationId, tasksOperationId,
            action.Id, operatorUserId, action.SequenceNumber, category, source, correlation);

        await TryDeliverAsync(action, cancellationToken);
        return action;
    }

    public async Task TryDeliverAsync(InterviewOutcomeRepairAction action, CancellationToken cancellationToken)
    {
        try
        {
            await auditDelivery.DeliverAsync(action, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Immediate audit delivery for interview outcome repair action {RecoveryActionId} failed; the durable intent remains outstanding for the background sweep. CompanyId={CompanyId} ReconciliationId={ReconciliationId}",
                action.Id, action.CompanyId, action.ReconciliationId);
        }
    }

    /// <summary>
    /// Durable recovery for a Tasks reset that was not followed by an unblock (crash, or a direct
    /// reset through the Tasks endpoint): any blocked reconciliation whose linked Tasks operation is
    /// no longer terminal is unblocked automatically.
    /// </summary>
    public async Task<int> UnblockResetTasksOperationsAsync(CancellationToken cancellationToken)
    {
        var blocked = await db.InterviewOutcomeTaskReconciliations
            .Where(r => r.BlockedAt != null
                && r.BlockedCategory == InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure
                && r.BlockedTasksOperationId != null)
            .OrderBy(r => r.BlockedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var unblocked = 0;
        foreach (var record in blocked)
        {
            try
            {
                var state = await tasksStateReader.GetAsync(
                    record.CompanyId, record.BlockedTasksOperationId!.Value, cancellationToken);

                if (state is null || state.IsTerminal || (state.ResetCount == 0 && state.AdjudicationCount == 0))
                    continue;

                var adjudicated = state.AdjudicationCount > 0;
                var action = await UnblockAsync(
                    record,
                    (adjudicated ? state.LastAdjudicatedBy : state.LastResetBy) ?? record.RecordedBy,
                    adjudicated ? TasksAdjudicationReason : TasksResetReason,
                    adjudicated ? InterviewOutcomeRepairAction.SourceTasksAdjudication : InterviewOutcomeRepairAction.SourceTasksReset,
                    cancellationToken);

                if (action is not null)
                    unblocked++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Could not auto-unblock interview outcome reconciliation after a Tasks reset; it will be retried. ReconciliationId={ReconciliationId} CompanyId={CompanyId} TasksOperationId={TasksOperationId}",
                    record.Id, record.CompanyId, record.BlockedTasksOperationId);
                db.Entry(record).State = EntityState.Detached;
            }
        }

        return unblocked;
    }
}
