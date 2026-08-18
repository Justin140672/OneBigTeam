using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Fixture for test classes that don't fit a single-fixed-persona fixture (RolePersonaFixtureBase /
/// OutlierPersonaFixtures) but that still DON'T need CrossUser's sequential execution: classes that
/// either never log in as a seeded persona at all (signup/marketing/verify-email flows), or that log
/// in as more than one persona across different test METHODS (never switching mid-test via
/// SwitchAccountAsync/SwitchPersonaAsync). Each such test method calls LoginPage.LoginAsync directly
/// with whichever persona it needs — LoginAsync itself is cache-aware for ANY persona via
/// PersonaLoginCache, so there's no real-login cost penalty to reusing a blank context here the way
/// there was before that cache existed. No storageState is pre-applied (there is no single canonical
/// persona for these classes), and — unlike CrossUserFixture — no full teardown delay is needed
/// either, since these tests never switch persona mid-test (an outlier LoginAsync call for a
/// different persona re-authenticates cleanly via cookie-clearing, same as the role-fixed fixtures).
///
/// Carries NO named [Collection], so test classes using it are IClassFixture-wired and run in
/// parallel with everything else, same as the role-fixed fixtures.
/// </summary>
public sealed class ParallelBlankPersonaFixture : IAsyncLifetime, IPersonaFixture
{
    private AppFixture? _app;

    public string WebBaseUrl => _app!.WebBaseUrl;
    public string MarketingBaseUrl => _app!.MarketingBaseUrl;
    public string ApiBaseUrl => _app!.ApiBaseUrl;
    public string AdminWebBaseUrl => _app!.AdminWebBaseUrl;
    public IBrowser Browser => _app!.Browser;
    public BrowserNewContextOptions? AuthenticatedContextOptions => null;
    public bool RequiresFullTeardownDelay => false;

    public async Task InitializeAsync() => _app = await SharedAppFixture.AcquireAsync();

    public async Task DisposeAsync() => await SharedAppFixture.ReleaseAsync();
}
