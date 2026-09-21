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
                var candidate = new AppFixture();
                // Only publish to the static field once InitializeAsync has actually succeeded. If it
                // throws (a transient Aspire/Postgres/Chromium startup failure), the caller that
                // triggered it fails loudly as expected, but _instance must stay null so the NEXT
                // caller gets a fresh attempt instead of permanently inheriting a half-initialized
                // instance (WebBaseUrl/Browser etc. still unset) — that previously cascaded into a
                // NullReferenceException for every other test in the run, since nothing ever retried.
                await candidate.InitializeAsync();

                // Pre-warm all 4 canonical personas' real logins SERIALLY, right here, before
                // publishing _instance — every one of the ~170 role-fixed test classes blocks on this
                // same _gate via RolePersonaFixtureBase.InitializeAsync -> AcquireAsync, so nothing else
                // can even reach PersonaLoginCache until this finishes. That turns what used to be a
                // run-start stampede (dozens of classes, overwhelmingly laura.bennett's ~110, all
                // racing PersonaLoginCache.GetOrLoginAsync the instant they unblock) into 4 sequential,
                // uncontended real logins against an otherwise-idle app. Several rounds of patching the
                // stampede's SYMPTOMS (widening app-shell wait timeouts, coalescing racing
                // invalidate+relogin callers) each helped but never fully closed it, because they were
                // all reacting to contention after the fact instead of removing the concurrent race that
                // causes it. This removes the race itself: by the time _gate is released and every
                // waiting class's InitializeAsync resumes, PersonaLoginCache already holds a good,
                // unstale entry for all 4 personas, so essentially no test should ever need a real login
                // (or the invalidate+relogin path) again for the rest of the run.
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
