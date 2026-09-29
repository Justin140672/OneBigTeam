using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

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
