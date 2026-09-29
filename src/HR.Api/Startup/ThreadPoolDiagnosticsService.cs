namespace HR.Api.Startup;

/// <summary>
/// E2E-only background diagnostic: logs .NET ThreadPool saturation every 5s. Added while chasing a
/// symptom where, under the 15-thread parallel E2E run, the shared HR.Api process goes completely
/// silent — no "HTTP ... started" log line for ANY request, for any circuit, not just one slow one —
/// while host-level CPU sampling (see HR.Web.E2E.Tests' ResourceSampler) shows the process at ~0%
/// CPU throughout. That combination (server accepts nothing, but isn't burning CPU) is the classic
/// signature of ThreadPool starvation: enough worker threads are parked on blocking/synchronous work
/// that no thread is free to even dequeue a new incoming request, and .NET's conservative thread-
/// injection algorithm (roughly one new thread every ~500ms once starvation is detected) recovers
/// far slower than requests are arriving under this load — which also matches "it passes under a
/// debugger" (breakpoints throttle the request rate low enough that injection can keep up).
///
/// This does not fix anything — it exists purely so the next time this happens, the API's own log
/// shows PendingWorkItemCount climbing and AvailableThreads bottoming out at the exact moment
/// requests stop being dispatched, instead of the silence looking identical to "server not running".
/// </summary>
internal sealed class ThreadPoolDiagnosticsService(ILogger<ThreadPoolDiagnosticsService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ThreadPool.GetAvailableThreads(out var availableWorker, out var availableIo);
                ThreadPool.GetMinThreads(out var minWorker, out var minIo);
                ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);

                logger.LogInformation(
                    "[ThreadPoolDiag] worker avail={AvailableWorker}/{MaxWorker} (min={MinWorker}) io avail={AvailableIo}/{MaxIo} (min={MinIo}) " +
                    "threadCount={ThreadCount} pendingWorkItems={PendingWorkItemCount} completedWorkItems={CompletedWorkItemCount}",
                    availableWorker, maxWorker, minWorker,
                    availableIo, maxIo, minIo,
                    ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, ThreadPool.CompletedWorkItemCount);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ThreadPoolDiag] sample failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
