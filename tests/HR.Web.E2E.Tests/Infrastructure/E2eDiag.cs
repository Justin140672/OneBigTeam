using System.Diagnostics;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Lightweight, always-on timing diagnostics for the login/app-shell path. Console.WriteLine (not
/// ITestOutputHelper) deliberately — the callers here (PersonaLoginCache, LoginPage) run outside any
/// single test's lifetime (shared bootstrap logins, cross-class cache gates), so there is no single
/// test's output sink to write to. `dotnet test ... --logger "console;verbosity=detailed"` captures
/// process Console output interleaved with the rest of the run, which is exactly what's needed to see
/// WHERE time actually goes (waiting on a gate/semaphore vs. real network/app-shell latency) instead of
/// only ever seeing the final class duration or a bare TimeoutException.
///
/// All timestamps are elapsed-since-process-start so they line up across the ~15 parallel threads
/// without needing wall-clock correlation.
/// </summary>
internal static class E2eDiag
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Log(string category, string message)
    {
        Console.WriteLine(
            $"[e2e-diag +{Clock.Elapsed.TotalSeconds,8:F3}s] [t{Environment.CurrentManagedThreadId,3}] [{category}] {message}");
    }

    public static IDisposable Time(string category, string message)
    {
        var start = Clock.Elapsed;
        Log(category, $"START {message}");
        return new Timer(category, message, start);
    }

    private sealed class Timer(string category, string message, TimeSpan start) : IDisposable
    {
        public void Dispose() =>
            Log(category, $"END   {message} ({(Clock.Elapsed - start).TotalSeconds:F3}s)");
    }
}
