using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class TopBarLogoutTests : IAsyncLifetime
{
    private const string Email = "laura.bennett@acme.example";

    private AppFixture _app = null!;
    private Microsoft.Playwright.IBrowserContext _context = null!;
    private Microsoft.Playwright.IPage _page = null!;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();
        _context = await _app.Browser.NewContextAsync(E2eBrowserContextOptions.Create());
        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(30_000);
        _page.SetDefaultNavigationTimeout(30_000);
    }

    public async Task DisposeAsync()
    {
        try { await _page.GotoAsync("about:blank"); } catch { }
        await _context.DisposeAsync();
        await SharedAppFixture.ReleaseAsync();
    }

    [Fact]
    public async Task TopBarLogout_EndsSessionAndReturnsToLogin()
    {
        var login = new LoginPage(_page, _app.WebBaseUrl);
        await login.GoToAsync();
        await login.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        await _page.GetByTestId("topbar-logout").ClickAsync();
        await _page.WaitForURLAsync(new Regex("/login"), new() { Timeout = 30_000 });

        await _page.GotoAsync($"{_app.WebBaseUrl}/dashboard/hr", new() { WaitUntil = Microsoft.Playwright.WaitUntilState.Commit });
        await _page.WaitForURLAsync(new Regex("/login"), new() { Timeout = 30_000 });
        Assert.Equal(0, await _page.Locator(".app-shell").CountAsync());
    }
}
