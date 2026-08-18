using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Base for the 4 role-fixed fixtures (HrAdmin/Manager/Recruiter/Employee). Each fixture instance
/// obtains its persona's authenticated Playwright storageState from <see cref="PersonaLoginCache"/>,
/// which performs the real UI login flow exactly ONCE PER PERSONA for the whole test run (not once
/// per fixture instance) and hands every caller the same cached storageState — the per-test UI login
/// round trip (page load + form fill + Supabase round trip) was the single biggest fixed per-test
/// cost in the old single shared-collection design, and repeating a real login per test CLASS (rather
/// than per persona) was itself a costly and, under load, failure-prone amount of duplicate work
/// (concurrent real Supabase logins timing out under contention).
///
/// Test classes wire this up via IClassFixture (not ICollectionFixture) and carry NO [Collection]
/// attribute, so each class gets its own default xUnit collection and different classes run in
/// parallel with each other (see xunit.runner.json's maxParallelThreads). That is what buys real
/// concurrency across the ~139 role-fixed test classes — xUnit v2 always runs the classes within a
/// single named collection sequentially, so keeping them in one shared "HrAdmin" collection would
/// have capped concurrency at 4 (one thread per role) instead of scaling with class count.
/// PersonaLoginCache is what makes that per-class fixture instantiation cheap: many fixture instances
/// across many parallel classes ask for the same persona, but only the first one actually logs in —
/// everyone else awaits that same in-flight/completed login.
///
/// A test method that needs a persona other than this fixture's canonical one (e.g. a one-off
/// access-denied check) still works correctly — LoginPage.LoginAsync detects the mismatch and clears
/// cookies before doing a real login for that persona, it just doesn't get the storageState speed-up
/// for that one call.
/// </summary>
public abstract class RolePersonaFixtureBase(string personaEmail) : IAsyncLifetime, IPersonaFixture
{
    private AppFixture? _app;
    private BrowserNewContextOptions? _authenticatedContextOptions;

    public string PersonaEmail { get; } = personaEmail;

    public string WebBaseUrl => _app!.WebBaseUrl;
    public string MarketingBaseUrl => _app!.MarketingBaseUrl;
    public string ApiBaseUrl => _app!.ApiBaseUrl;
    public string AdminWebBaseUrl => _app!.AdminWebBaseUrl;
    public IBrowser Browser => _app!.Browser;
    public BrowserNewContextOptions? AuthenticatedContextOptions => _authenticatedContextOptions;
    public bool RequiresFullTeardownDelay => false;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();

        // Real UI login happens at most once per persona for the whole run — see PersonaLoginCache.
        // Every test context built from AuthenticatedContextOptions starts pre-authenticated.
        _authenticatedContextOptions = await PersonaLoginCache.GetOrLoginAsync(_app, PersonaEmail);
    }

    public async Task DisposeAsync() => await SharedAppFixture.ReleaseAsync();
}

/// <summary>HR Administrator persona — Laura Bennett.</summary>
public sealed class HrAdminPersonaFixture() : RolePersonaFixtureBase("laura.bennett@acme.example");

/// <summary>Manager persona — James Okafor (David Park's dual HR/manager tests use the outlier-persona fallback).</summary>
public sealed class ManagerPersonaFixture() : RolePersonaFixtureBase("james.okafor@acme.example");

/// <summary>Recruiter persona — Marcus Diallo.</summary>
public sealed class RecruiterPersonaFixture() : RolePersonaFixtureBase("marcus.diallo@acme.example");

/// <summary>Plain Employee persona — Tom Williams.</summary>
public sealed class EmployeePersonaFixture() : RolePersonaFixtureBase("tom.williams@acme.example");
