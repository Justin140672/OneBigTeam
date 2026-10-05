namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Process-wide holder of the one <see cref="AppFixture"/> (Aspire app + Postgres + shared browser) used
/// by every xUnit collection in this assembly. xUnit creates a fresh fixture instance per collection/
/// class with no built-in way to share one, so each fixture acquires this shared instance instead of
/// owning an AppFixture directly.
///
/// Startup is single-shot: one cached task, for success AND failure. Every caller awaits the same task,
/// so a failed startup surfaces the same original exception to everyone and never triggers another
/// Aspire boot. The failed candidate is diagnosed and disposed inside the attempt.
///
/// Release is a no-op: the app must survive every test that might still run; cleanup happens
/// best-effort via ProcessExit.
/// </summary>
internal static class SharedAppFixture
{
    private static readonly SharedStartup<AppFixture> Startup = new(CreateAppAsync);
    private static int _exitHookRegistered;

    public static Task<AppFixture> AcquireAsync()
    {
        if (Interlocked.Exchange(ref _exitHookRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeOnExit();
        }

        return Startup.GetAsync();
    }

    public static Task ReleaseAsync() => Task.CompletedTask;

    private static Task<AppFixture> CreateAppAsync()
    {
        ResourceSampler.StartOnce();

        return StartupAttempt.RunAsync(
            create: () => new AppFixture(),
            initialize: async candidate =>
            {
                await candidate.InitializeAsync();

                foreach (var personaEmail in new[]
                {
                    "laura.bennett@acme.example",
                    "james.okafor@acme.example",
                    "marcus.diallo@acme.example",
                    "tom.williams@acme.example",
                })
                {
                    await PersonaLoginCache.GetOrLoginAsync(candidate.Browser, candidate.WebBaseUrl, personaEmail);
                }
            },
            captureDiagnostics: (candidate, failure) => candidate.CaptureStartupDiagnosticsAsync(failure),
            dispose: candidate => new ValueTask(candidate.DisposeAsync()),
            log: message => E2eDiag.Log("StartupDiag", message));
    }

    private static void DisposeOnExit()
    {
        try
        {
            var task = Startup.PeekStartedTask();
            if (task is { IsCompletedSuccessfully: true })
            {
                task.Result.DisposeAsync().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // best-effort process-exit cleanup
        }
    }
}
