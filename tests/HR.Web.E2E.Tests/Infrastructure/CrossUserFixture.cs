using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Fixture for the "CrossUser" collection — tests that switch persona mid-test via the dev persona
/// switcher (LoginPage.SwitchAccountAsync/SwitchPersonaAsync), plus any test that doesn't cleanly map
/// to one of the 4 named role personas. No storageState here: every test logs in fresh via the real
/// UI flow, same as the original single-collection design, because these tests genuinely need to
/// authenticate as more than one persona (or a persona outside the 4 role fixtures) within their
/// lifetime.
/// </summary>
public sealed class CrossUserFixture : IAsyncLifetime, IPersonaFixture
{
    private AppFixture? _app;

    public string WebBaseUrl => _app!.WebBaseUrl;
    public string MarketingBaseUrl => _app!.MarketingBaseUrl;
    public string ApiBaseUrl => _app!.ApiBaseUrl;
    public string AdminWebBaseUrl => _app!.AdminWebBaseUrl;
    public IBrowser Browser => _app!.Browser;
    public BrowserNewContextOptions? AuthenticatedContextOptions => null;
    public bool RequiresFullTeardownDelay => true;

    public async Task InitializeAsync() => _app = await SharedAppFixture.AcquireAsync();

    public async Task DisposeAsync() => await SharedAppFixture.ReleaseAsync();
}
