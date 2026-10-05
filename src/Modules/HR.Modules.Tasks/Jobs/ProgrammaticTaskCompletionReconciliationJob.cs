using Hangfire;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Jobs;

/// <summary>
/// Drives unconfirmed <c>ProgrammaticTaskCompletion</c> rows (tasks completed by another module via
/// ITaskCompleter/ITaskResolution) to confirmation.
///
/// Reconciliation ownership, so the two Tasks jobs never wait on each other:
///  - ProgrammaticTaskCompletionReconciliationJob owns ONLY programmatic completion rows. It never
///    reads or waits on a TaskCompletionOperation.
///  - TaskCompletionReconciliationJob / TaskCompletionEffectsJob own ONLY TaskCompletionOperation
///    rows (Pending, DispatchApplied). They never read programmatic completion rows.
///  - A programmatic completion is only created when the task has no active (non-Rejected)
///    operation; while one is Pending or DispatchApplied TaskCompleter reports "unconfirmed" and
///    creates no state, and the operation jobs resume it. A Processed operation counts as
///    confirmed; a Rejected one is ignored.
///  - Recruitment's reconciliation waits on Tasks (never the reverse), so there is no cycle.
///
/// Exclusivity comes from the row's claim lease (see TaskCompleter.ReconcileAsync); permanently
/// failed rows are skipped here and surfaced by an error log when they were marked.
/// </summary>
internal sealed class ProgrammaticTaskCompletionReconciliationJob(
    TasksDbContext dbContext,
    TaskCompleter completer,
    IClock clock,
    ILogger<ProgrammaticTaskCompletionReconciliationJob> logger)
{
    private const int BatchSize = 100;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var unconfirmed = await dbContext.ProgrammaticTaskCompletions
            .Where(c => c.ConfirmedAt == null
                && c.TerminalFailureAt == null
                && (c.ClaimedUntil == null || c.ClaimedUntil < now))
            .OrderBy(c => c.CreatedAt)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var state in unconfirmed)
        {
            var task = await dbContext.TaskItems.SingleOrDefaultAsync(t => t.Id == state.TaskId && t.CompanyId == state.CompanyId);
            if (task is null)
                continue;

            try
            {
                await completer.ReconcileAsync(task, state, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Programmatic task completion for task {TaskId} (company {CompanyId}, operation {OperationId}) could not be confirmed and will be retried.",
                    state.TaskId, state.CompanyId, state.OperationId);
            }
        }
    }
}
