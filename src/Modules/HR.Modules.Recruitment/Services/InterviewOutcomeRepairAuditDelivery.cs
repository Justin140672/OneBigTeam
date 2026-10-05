using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

internal sealed class InterviewOutcomeRepairAuditDelivery(
    RecruitmentDbContext db,
    IAuditEventPublisher auditPublisher,
    IAuditEventExistenceReader auditExistenceReader,
    IClock clock,
    ILogger<InterviewOutcomeRepairAuditDelivery> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    private const int BatchSize = 100;

    /// <summary>
    /// Delivers the durable repair audit intent and records the delivery checkpoint only once the
    /// deterministic event is seen to exist; the publisher swallows persistence failures, so a normal
    /// return proves nothing. Never throws for a delivery failure: the intent stays outstanding.
    /// </summary>
    public async Task<bool> DeliverAsync(InterviewOutcomeRepairAction action, CancellationToken cancellationToken)
    {
        if (action.IsDelivered)
            return true;

        try
        {
            if (!action.TryClaim(clock.UtcNowOffset()))
                return false;

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(action).State = EntityState.Detached;
            return false;
        }
        catch (Exception ex)
        {
            db.Entry(action).State = EntityState.Detached;
            LogFailure(action, ex);
            return false;
        }

        var restoredContext = action.CorrelationId is { } correlationId
            ? ExecutionContextInfo.Restore(
                correlationId.ToString("D"), Guid.NewGuid(), null, ExecutionOrigin.ReconciliationJob)
            : null;
        using var _ = restoredContext is null ? null : executionContextAccessor?.Push(restoredContext);

        try
        {
            if (!await auditExistenceReader.ExistsAsync(action.Id, cancellationToken))
            {
                await auditPublisher.PublishAsync(
                    new InterviewOutcomeReconciliationRepairedAuditEvent(
                        action.CompanyId, action.ReconciliationId, action.InterviewId, action.ApplicationId,
                        action.OperatorUserId, action.BlockedCategory, action.TasksOperationId, action.Reason,
                        action.SequenceNumber, action.OccurredAt),
                    cancellationToken);

                if (!await auditExistenceReader.ExistsAsync(action.Id, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"The interview outcome repair audit event {action.Id} was not confirmed as persisted.");
                }
            }

            action.MarkDelivered(clock.UtcNowOffset());
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(action, ex);

            try
            {
                action.MarkDeliveryFailed(ex.Message, clock.UtcNowOffset());
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx,
                    "Could not record the audit delivery failure for interview outcome repair action {RecoveryActionId}; its lease will expire and delivery will be retried.",
                    action.Id);
                db.Entry(action).State = EntityState.Detached;
            }

            return false;
        }
    }

    public async Task<int> DeliverOutstandingAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var outstanding = await db.InterviewOutcomeRepairActions
            .Where(a => a.AuditDeliveredAt == null && (a.ClaimedUntil == null || a.ClaimedUntil < now))
            .OrderBy(a => a.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var delivered = 0;
        foreach (var action in outstanding)
        {
            if (await DeliverAsync(action, cancellationToken))
                delivered++;
        }

        return delivered;
    }

    private void LogFailure(InterviewOutcomeRepairAction action, Exception ex) =>
        logger.LogWarning(ex,
            "Interview outcome repair audit delivery is unconfirmed and will be retried. RecoveryActionId={RecoveryActionId} CompanyId={CompanyId} InterviewId={InterviewId} ReconciliationId={ReconciliationId} TasksOperationId={TasksOperationId} RepairCount={RepairCount} AttemptCount={AttemptCount} FailureCategory={FailureCategory} CorrelationId={CorrelationId}",
            action.Id, action.CompanyId, action.InterviewId, action.ReconciliationId, action.TasksOperationId,
            action.SequenceNumber, action.AuditAttemptCount + 1, ex.GetType().Name, action.CorrelationId);
}
