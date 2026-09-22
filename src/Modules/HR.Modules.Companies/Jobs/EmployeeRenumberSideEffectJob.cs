using Hangfire;
using HR.Modules.Companies.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Companies.Jobs;

/// <summary>
/// SET-08: enqueue-style (one-off, fired per outbox row) job that performs the actual employee
/// renumbering side effect after an employee-number FORMAT change, asynchronously and reliably.
/// Mirrors HR.Modules.Notifications.Jobs.EmailDeliveryJob's shape (idempotency-by-row-status,
/// [AutomaticRetry], attempt tracking, final-attempt-marks-Failed-and-rethrows).
///
/// Idempotency/retry safety: <see cref="IEmployeeRenumberingService.RenumberAllEmployeesAsync"/>
/// itself is already idempotent (deterministic CreatedAt order + the same number generator always
/// produces the same result for a given format), so re-running it after a crash or a Hangfire retry
/// is always safe — this job's own idempotency guard (no-op if the row is already Processed) exists
/// purely to avoid redundant work/audit noise, not because a duplicate run would corrupt data.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class EmployeeRenumberSideEffectJob(
    CompaniesDbContext db,
    IEmployeeRenumberingService employeeRenumberingService,
    IClock clock,
    ILogger<EmployeeRenumberSideEffectJob> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public const int MaxAttempts = 4;

    public async Task ProcessAsync(Guid outboxMessageId, Guid companyId)
    {
        var message = await db.OutboxMessages.SingleOrDefaultAsync(m => m.Id == outboxMessageId);

        if (message is null)
        {
            logger.LogWarning(
                "EmployeeRenumberSideEffectJob: no outbox message found for id {OutboxMessageId} — skipping.",
                outboxMessageId);
            return;
        }

        // OBT-REM-11: verify the caller-supplied companyId (used by the Hangfire failure-audit
        // filter to scope this job to a tenant) actually matches the outbox row being processed.
        if (message.CompanyId != companyId)
        {
            logger.LogError(
                "EmployeeRenumberSideEffectJob: company mismatch for outbox message {OutboxMessageId} — job argument {ArgCompanyId} does not match message's company {ActualCompanyId}.",
                outboxMessageId, companyId, message.CompanyId);
            throw new InvalidOperationException(
                $"OutboxMessage {outboxMessageId} does not belong to company {companyId}.");
        }

        // Idempotency guard: already completed (possibly by a prior attempt that succeeded but
        // crashed before this status update, or a duplicate enqueue) — no-op.
        if (message.Status == Domain.OutboxMessage.StatusProcessed)
            return;

        var now = clock.UtcNowOffset();
        message.MarkProcessing(now);
        await db.SaveChangesAsync();

        // Ticket 23 (P2): restore the persisted correlation/causation/message ids for the duration
        // of this resumed background work (Origin = ReconciliationJob), so anything the renumbering
        // service logs/audits downstream carries the SAME correlation id as the original settings
        // change even after a process restart. Rows written before this migration (all-null
        // metadata) fall back to a fresh root context.
        var restoredContext = message.CorrelationId is { } correlationId
            ? ExecutionContextInfo.Restore(
                correlationId.ToString("D"), message.MessageId ?? Guid.NewGuid(), message.CausationId,
                ExecutionOrigin.ReconciliationJob)
            : ExecutionContextInfo.NewRoot(ExecutionOrigin.ReconciliationJob);
        using var _ = executionContextAccessor?.Push(restoredContext);

        try
        {
            await employeeRenumberingService.RenumberAllEmployeesAsync(message.CompanyId, CancellationToken.None);

            var completedAt = clock.UtcNowOffset();
            message.MarkProcessed(completedAt);
            await db.SaveChangesAsync();

            logger.LogInformation(
                "EmployeeRenumberSideEffectJob: employee renumbering completed for company {CompanyId} (outbox {OutboxMessageId}).",
                message.CompanyId, outboxMessageId);
        }
        catch (Exception ex)
        {
            var isFinalAttempt = message.AttemptCount >= MaxAttempts;

            if (isFinalAttempt)
            {
                message.MarkFailed("Employee renumbering failed.", clock.UtcNowOffset());
                await db.SaveChangesAsync();

                logger.LogError(ex,
                    "EmployeeRenumberSideEffectJob: employee renumbering permanently failed after {Attempts} attempts for company {CompanyId} (outbox {OutboxMessageId}). Retry via RetryEmployeeRenumberSideEffect.",
                    MaxAttempts, message.CompanyId, outboxMessageId);
            }
            else
            {
                logger.LogWarning(ex,
                    "EmployeeRenumberSideEffectJob: employee renumbering attempt {AttemptCount} failed for company {CompanyId} (outbox {OutboxMessageId}) — will retry.",
                    message.AttemptCount, message.CompanyId, outboxMessageId);
            }

            // Rethrow while retries remain so Hangfire schedules the next attempt; rethrow on the
            // final attempt too so the existing BackgroundJobAuditFilter records the standard
            // operational-failure audit trail every other job already relies on.
            throw;
        }
    }
}
