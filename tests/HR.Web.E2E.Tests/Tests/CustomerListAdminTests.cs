using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers HR.Admin.Web's read-only Customer List page (/customers — CustomerList.razor):
/// - An allow-listed platform admin sees the tenant grid with the seeded Acme row.
/// - A search term that matches nothing resolves to an empty grid (Syncfusion ".e-emptyrow"),
///   not a crash and not stale rows.
/// - A valid dev-login persona not on "PlatformAdmin:AllowedEmails" is rejected server-side and
///   sees the dashboard-error banner, not customer data.
/// - A genuinely anonymous visitor is redirected to /login.
///
/// Row-click navigation into CustomerDetails is already covered by
/// <see cref="CustomerDetailsPageTests"/>. This page is view-only by design — no create/edit/delete.
/// </summary>
public sealed class CustomerListAdminTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private const string AllowListedAdminEmail = "priya.shah@acme.example";
    private const string NonAllowListedEmail = "tom.williams@acme.example";

    [Fact]
    public async Task AllowListedAdmin_SeesTenantGridWithSeededCompany()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var list = new CustomerListPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await list.GoToAsync();

        Assert.False(await list.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see the customer grid, not the error banner");
        Assert.True(await list.HasCompanyAsync("Acme Corporation"),
            "Expected the seeded Acme Corporation row in the customer list");
    }

    [Fact]
    public async Task Search_WithNoMatch_ShowsEmptyGrid_NotStaleRows()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var list = new CustomerListPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await list.GoToAsync();
        Assert.True(await list.HasCompanyAsync("Acme Corporation"));

        // Debounced (300ms) SfTextBox search — see CustomerList.razor's OnSearchChanged. SfTextBox's
        // ValueChanged fires on the native change event (focus loss), not per keystroke, so a bare
        // FillAsync never triggers it — Tab out to commit the value.
        var searchInput = _page.Locator(".customer-search-box input");
        await searchInput.FillAsync($"no-such-tenant-{Guid.NewGuid():N}");
        await searchInput.PressAsync("Tab");
        await _page.WaitForTimeoutAsync(600);
        await _page.WaitForSelectorAsync(".e-grid .e-emptyrow, .e-grid .e-row, .dashboard-error", new() { Timeout = 15_000 });

        Assert.False(await list.IsErrorBannerVisibleAsync(),
            "A no-match search must not surface an error banner");
        Assert.True(await _page.Locator(".e-grid .e-emptyrow").CountAsync() > 0
                    || await _page.Locator(".e-grid .e-row").CountAsync() == 0,
            "Expected an empty grid for a search term that matches no tenant");
        Assert.False(await list.HasCompanyAsync("Acme Corporation"),
            "The previously visible Acme row must not remain after a no-match search");
    }

    [Fact]
    public async Task NonAllowListedPersona_IsRejectedAtLogin_NotGivenCustomerAccess()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        var error = await login.SubmitExpectingNotAuthorisedAsync(NonAllowListedEmail);

        Assert.Contains("not authorised", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(login.IsOnLoginPage(),
            "A non-allow-listed account must be rejected on the login page, not handed a session");
    }

    [Fact]
    public async Task AnonymousAccess_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/customers");
        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
