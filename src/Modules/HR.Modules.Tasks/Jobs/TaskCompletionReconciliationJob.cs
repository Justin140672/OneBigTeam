using Hangfire;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Jobs;

/// <summary>
/// Ticket 11 (P1): recurring repair for <see cref="TaskCompletionOperation"/> rows abandoned by
/// CompleteTaskHandler (see Features/CompleteTask/Handler.cs) partway through completion —
/// specifically:
///
///  - Pending: the request was persisted but the process was interrupted before the business
///    dispatch ran (or before its outcome was recorded). This job replays the SAME dispatch using
///    the operation's own persisted actor/decision/reason (never the caller's original request,
///    which is gone) and, on success, completes the TaskItem — exactly what the handler itself
///    would have done next. On rejection, the operation is marked Rejected (terminal) and the
///    TaskItem is left untouched, same as an inline rejection.
///  - DispatchApplied: the business dispatch succeeded and the TaskItem was already completed, but
///    the notification/audit side effects were never confirmed and no in-flight
///    TaskCompletionEffectsJob attempt is known to be running (the handler's own inline catch
///    already enqueues one on a known failure — this covers the case where the process died before
///    even reaching that catch block). Re-enqueuing is safe: TaskCompletionEffectsJob no-ops on an
///    already-Processed operation and re-checks notification existence before writing.
///
/// A dispatch replay for a Pending operation reuses the SAME operation identity as
/// <see cref="TaskCompletionContext.DispatchOperationId"/>, so any action with its own
/// idempotency-keyed side effect (e.g. AssetTaskCompletionAction's "Return asset" task,
/// LeaveTaskCompletionAction's approve/reject call) converges on its original result rather than
/// repeating it.
///
/// Ticket 19 (P2): the fixed "no progress within 5 minutes" age check is now only the ELIGIBILITY
/// filter (still needed so a genuinely in-flight CompleteTaskHandler call, which normally completes
/// in milliseconds, is never raced) — the actual exclusivity guarantee comes from
/// <see cref="TaskCompletionOperation.Claim"/>, guarded by the same optimistic-concurrency
/// primitive as AccountDisablement/CandidateDocumentDeletionOperation. Because
/// <see cref="TaskCompletionOperation.Version"/> is a real concurrency token, this ALSO protects
/// against a live CompleteTaskHandler request racing this sweep for the SAME operation — the
/// handler's own plain SaveChangesAsync call already fails with DbUpdateConcurrencyException if the
/// reconciler's claim commits first, with no changes required on that side.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class TaskCompletionReconciliationJob(
    TasksDbContext dbContext,
    TaskCompletionDispatcher dispatcher,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<TaskCompletionReconciliationJob> logger)
{
    public const int MaxAttempts = 4;

    /// <summary>
    /// A Pending operation younger than this is assumed to still be genuinely in-flight inside a
    /// normal CompleteTaskHandler call (which completes in milliseconds under healthy conditions),
    /// not abandoned — avoids attempting to claim (and therefore contending with) a live request.
    /// </summary>
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromMinutes(5);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var cutoff = now - StalePendingThreshold;
        var reconcilerInstanceId = Guid.NewGuid();

        // Ticket 19 (P2): the LeaseExpiresAt check is required, not just the Status/age filter —
        // Claim() deliberately leaves Status as Pending (see its remarks), so a row a prior sweep
        // (or this same sweep, on a slow replay) already claimed and is still actively replaying
        // would otherwise remain "eligible" here and get claimed a second time by a LATER,
        // independent query — the guarded save on the SECOND claim only protects against a truly
        // concurrent attempt against the SAME already-fetched row, not a fresh query issued after
        // the first claim has already committed.
        var stalePending = await dbContext.TaskCompletionOperations
            .Where(o => o.Status == TaskCompletionOperation.StatusPending
                && o.CreatedAt < cutoff
                && (o.LeaseExpiresAt == null || o.LeaseExpiresAt < now))
            .ToListAsync();

        foreach (var operation in stalePending)
        {
            await ReplayPendingAsync(operation, reconcilerInstanceId, now);
        }

        var abandonedDispatchApplied = await dbContext.TaskCompletionOperations
            .Where(o => o.Status == TaskCompletionOperation.StatusDispatchApplied
                && (o.LeaseExpiresAt == null || o.LeaseExpiresAt < now))
            .ToListAsync();

        foreach (var operation in abandonedDispatchApplied)
        {
            var expectedVersion = operation.Version;
            operation.Claim(reconcilerInstanceId, now);

            var claimResult = await dbContext.SaveChangesWithConcurrencyAsync(
                operation, expectedVersion,
                "This task completion operation was already claimed by another reconciler.",
                CancellationToken.None);

            if (claimResult.IsFailure)
            {
                logger.LogInformation(
                    "TaskCompletionReconciliationJob: lost the claim race for abandoned DispatchApplied operation {OperationId} (task {TaskId}, company {CompanyId}) — skipping this sweep.",
                    operation.Id, operation.TaskId, operation.CompanyId);
                continue;
            }

            logger.LogWarning(
                "TaskCompletionReconciliationJob: re-enqueuing abandoned DispatchApplied operation {OperationId} (task {TaskId}, company {CompanyId}) for side-effect confirmation.",
                operation.Id, operation.TaskId, operation.CompanyId);

            backgroundJobClient.Enqueue<TaskCompletionEffectsJob>(
                job => job.ProcessAsync(operation.Id, operation.CompanyId));
        }
    }

    private async Task ReplayPendingAsync(TaskCompletionOperation operation, Guid reconcilerInstanceId, DateTimeOffset now)
    {
        // Ticket 19 (P2): claim BEFORE touching anything else — guards against a live
        // CompleteTaskHandler request (or another reconciler replica) racing this exact replay.
        var expectedVersion = operation.Version;
        operation.Claim(reconcilerInstanceId, now);

        var claimResult = await dbContext.SaveChangesWithConcurrencyAsync(
            operation, expectedVersion,
            "This task completion operation was already claimed by another worker.",
            CancellationToken.None);

        if (claimResult.IsFailure)
        {
            logger.LogInformation(
                "TaskCompletionReconciliationJob: lost the claim race for stale pending operation {OperationId} (task {TaskId}, company {CompanyId}) — skipping this sweep.",
                operation.Id, operation.TaskId, operation.CompanyId);
            return;
        }

        var task = await dbContext.TaskItems.SingleOrDefaultAsync(t => t.Id == operation.TaskId);

        if (task is null)
        {
            logger.LogWarning(
                "TaskCompletionReconciliationJob: TaskItem {TaskId} for stale pending operation {OperationId} no longer exists — marking rejected.",
                operation.TaskId, operation.Id);
            operation.MarkRejected("The underlying task no longer exists.", now);
            await dbContext.SaveChangesAsync();
            return;
        }

        if (task.Status == Domain.TaskItemStatus.Completed)
        {
            // The TaskItem was already completed by some other path (shouldn't normally happen for
            // a Pending operation, but leaves nothing to redo) — converge the operation forward
            // rather than leaving it stuck.
            operation.MarkDispatchApplied(now);
            await dbContext.SaveChangesAsync();
            return;
        }

        logger.LogWarning(
            "TaskCompletionReconciliationJob: replaying abandoned pending completion operation {OperationId} for task {TaskId} (company {CompanyId}).",
            operation.Id, operation.TaskId, operation.CompanyId);

        var dispatchResult = await dispatcher.DispatchAsync(new TaskCompletionContext(
            task.CompanyId,
            task.Id,
            task.Title,
            task.Description,
            task.Source,
            task.ActionType,
            task.AssignedEmployeeId,
            operation.CompletedBy,
            now,
            task.SourceEntityId,
            operation.OutcomeDecision,
            operation.OutcomeReason,
            operation.Id), CancellationToken.None);

        if (!dispatchResult.IsSuccess)
        {
            operation.MarkRejected(dispatchResult.Error.Message, now);
            await dbContext.SaveChangesAsync();
            return;
        }

        operation.MarkDispatchApplied(now);
        task.Complete(operation.CompletedBy, now);
        await dbContext.SaveChangesAsync();

        // Side effects (notification/audit) are not attempted inline here — handing off to
        // TaskCompletionEffectsJob keeps this job focused purely on the business dispatch + task
        // completion, and the next sweep's "abandoned DispatchApplied" pass will pick this operation
        // up if that job doesn't run to completion on its own.
        backgroundJobClient.Enqueue<TaskCompletionEffectsJob>(
            job => job.ProcessAsync(operation.Id, operation.CompanyId));
    }
}
