namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Singleton wrapping the one <see cref="AppFixture"/> (Aspire app + Postgres + shared browser) used
/// by every xUnit collection in this assembly (HrAdmin/Manager/Recruiter/Employee/CrossUser).
/// xUnit's ICollectionFixture/IClassFixture create a fresh fixture instance per collection/class with
/// no built-in way to share one instance across them (that's an assembly-fixture feature this project
/// doesn't take a dependency on), so each collection/class fixture acquires this shared instance
/// instead of owning an AppFixture directly.
///
/// Deliberately NOT reference-counted to a mid-run teardown: with IClassFixture, ~139 role-fixed test
/// classes each acquire/release their own fixture instance independently as they start/finish, fully
/// interleaved by the test runner. There is no guarantee another class is still "holding" the app at
/// any given moment — a ref-counted "dispose when count hits zero" (the previous implementation) tears
/// the real Aspire app/Postgres/browser down under any test that's mid-flight the instant a transient
/// gap in overlap occurs, then pays a full Aspire boot again for whatever runs next. That is what
/// caused widespread random timeouts (not just login) once real per-class parallelism went live.
///
/// Instead: the app is created once, lazily, on first acquire, and lives for the rest of the process.
/// Release is a no-op — cleanup happens best-effort via ProcessExit, since correctness (the app must
/// survive every test that might still run) matters far more here than tidiness of an OS process/
/// container that the OS reclaims when the test host exits anyway.
/// </summary>
internal static class SharedAppFixture
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static AppFixture? _instance;
    private static bool _exitHookRegistered;

    public static async Task<AppFixture> AcquireAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_instance is null)
            {
                ResourceSampler.StartOnce();

                var candidate = new AppFixture();
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

                _instance = candidate;
            }

            if (!_exitHookRegistered)
            {
                _exitHookRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => _instance?.DisposeAsync().GetAwaiter().GetResult();
            }

            return _instance;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static Task ReleaseAsync() => Task.CompletedTask;
}
