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
///
/// Every line is also appended to files under <see cref="DiagDirectory"/> so CI can upload them.
/// </summary>
internal static class E2eDiag
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object FileGate = new();

    public static string DiagDirectory { get; } =
        Environment.GetEnvironmentVariable("E2E_DIAG_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "diag");

    public static void Log(string category, string message)
    {
        var line = $"[e2e-diag +{Clock.Elapsed.TotalSeconds,8:F3}s] [t{Environment.CurrentManagedThreadId,3}] [{category}] {message}";
        Console.WriteLine(line);
        AppendFile(category == "ResourceStats" ? "resource-sampling.log" : "e2e-diag.log", line);
    }

    public static void AppendFile(string relativePath, string content)
    {
        try
        {
            lock (FileGate)
            {
                var path = Path.Combine(DiagDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, content + Environment.NewLine);
            }
        }
        catch
        {
            // diagnostics must never fail a run
        }
    }

    public static void WriteFile(string relativePath, string content)
    {
        try
        {
            lock (FileGate)
            {
                var path = Path.Combine(DiagDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
        }
        catch
        {
            // diagnostics must never fail a run
        }
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
