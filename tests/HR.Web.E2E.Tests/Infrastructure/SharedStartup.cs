namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Single-shot, cached startup. The factory runs at most once per instance; the resulting task is
/// cached for success AND failure so every caller observes the same outcome and nothing is retried.
/// </summary>
internal sealed class SharedStartup<T>(Func<Task<T>> factory) where T : class
{
    private readonly Lazy<Task<T>> _startup =
        new(() => Task.Run(factory), LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<T> GetAsync() => _startup.Value;

    public Task<T>? PeekStartedTask() => _startup.IsValueCreated ? _startup.Value : null;
}

internal static class StartupAttempt
{
    /// <summary>
    /// Creates one candidate and initializes it. On failure the diagnostics are captured first, then the
    /// candidate is disposed exactly once; the original exception is always the one rethrown.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        Func<T> create,
        Func<T, Task> initialize,
        Func<T, Exception, Task> captureDiagnostics,
        Func<T, ValueTask> dispose,
        Action<string>? log = null)
    {
        var candidate = create();
        try
        {
            await initialize(candidate);
            return candidate;
        }
        catch (Exception failure)
        {
            try { await captureDiagnostics(candidate, failure); }
            catch (Exception diagnosticFailure)
            {
                log?.Invoke($"diagnostics capture failed: {diagnosticFailure.GetType().Name}: {diagnosticFailure.Message}");
            }

            try { await dispose(candidate); }
            catch (Exception disposeFailure)
            {
                log?.Invoke($"cleanup of failed startup candidate failed: {disposeFailure.GetType().Name}: {disposeFailure.Message}");
            }

            throw;
        }
    }
}
