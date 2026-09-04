using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR.Admin.Web's read-only Failed Payments Dashboard (/failed-payments):
/// - The "Failed Payments" nav link is present and navigates correctly for an allow-listed
///   platform admin.
/// - The search box and native status-filter &lt;select&gt; are present and interactable without
///   throwing/crashing the page.
/// - In this dev/test environment Stripe is not configured, so the page is expected to show the
///   "Stripe is not configured" dashboard-error state rather than real grid data — see
///   FailedPayments.razor's `!_response.StripeConfigured` branch.
/// - Anonymous and non-allow-listed access is rejected the same way CustomerList/CustomerDetails
///   are (dashboard-error banner, not a crash).
///
/// This page is explicitly view-only/reporting (see FailedPayments.razor) — there is no
/// create/edit/delete flow to cover here, only load/search/filter/navigate behavior.
/// </summary>
public sealed class FailedPaymentsDashboardTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    // Seeded platform-admin allow-listed persona — see appsettings.Development.json's
    // "PlatformAdmin:AllowedEmails" and DevPersonaStore, and AdminLoginPage's remarks on the
    // dev-login stub vs. server-side platform-admin authorisation being separate checks.
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    // Seeded plain-Employee persona (no platform-admin allow-list entry) — valid dev-login
    // credentials, but the server-side "PlatformAdmin:AllowedEmails" check should still reject
    // every subsequent API call this persona makes against the Admin Portal.
    private const string NonAllowListedEmail = "tom.williams@acme.example";

    [Fact]
    public async Task NavLink_IsVisible_AndNavigatesToFailedPayments()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        // LoginAsync only waits for the URL to leave /login — on this Blazor Server layout, the
        // nav (inside <AuthorizeView><Authorized>) renders after an additional async round trip
        // to resolve auth state, which can land a beat after the URL has already changed. Reading
        // IsVisibleAsync() immediately (it doesn't auto-wait/retry the way Playwright's action
        // methods do) can sample the DOM before that content has rendered at all — wait for the
        // link the same way navLink.ClickAsync() below already implicitly does.
        var navLink = _page.GetByRole(AriaRole.Link, new() { Name = "Failed Payments" });
        await navLink.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await navLink.ClickAsync();

        Assert.Contains("/failed-payments", _page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectUrl_AllowListedAdmin_ShowsStripeNotConfiguredState()
    {
        // No live Stripe key is configured in this dev/test environment, so
        // GetFailedPaymentsResponse.StripeConfigured is expected to be false — see
        // FailedPayments.razor's remarks and the class summary above.
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var dashboard = new FailedPaymentsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await dashboard.GoToAsync();

        Assert.True(await dashboard.IsErrorBannerVisibleAsync(),
            "Expected the 'Stripe is not configured' dashboard-error state for an allow-listed " +
            "admin in this environment (no live Stripe key configured)");
        var text = await dashboard.GetErrorBannerTextAsync() ?? "";
        Assert.Contains("Stripe", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not configured", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchBox_AndStatusFilter_ArePresentAndInteractable()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var dashboard = new FailedPaymentsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await dashboard.GoToAsync();

        Assert.True(await dashboard.SearchBox.IsVisibleAsync(),
            "Expected the company-name search box to be visible");
        Assert.True(await dashboard.StatusFilterSelect.IsVisibleAsync(),
            "Expected the native status-filter <select> to be visible");

        // Typing/selecting must not crash the page even though Stripe isn't configured and the
        // grid never actually renders in this environment — LoadAsync should just re-resolve to
        // the same dashboard-error state.
        await dashboard.SearchAsync("Acme");
        Assert.True(await dashboard.IsErrorBannerVisibleAsync(),
            "Expected the Stripe-not-configured state to persist after a search");

        await dashboard.SelectStatusFilterAsync("open");
        Assert.Equal("open", await dashboard.StatusFilterSelect.InputValueAsync());
        Assert.True(await dashboard.IsErrorBannerVisibleAsync(),
            "Expected the Stripe-not-configured state to persist after changing the status filter");

        await dashboard.SelectStatusFilterAsync("uncollectible");
        Assert.Equal("uncollectible", await dashboard.StatusFilterSelect.InputValueAsync());

        await dashboard.SelectStatusFilterAsync("");
        Assert.Equal("", await dashboard.StatusFilterSelect.InputValueAsync());
    }

    [Fact]
    public async Task NonAllowListedPersona_IsRejectedAtLogin_NotGivenFailedPaymentAccess()
    {
        // Mirrors CustomerDetailsPageTests.NonAllowListedPersona_IsRejectedAtLogin_NotGivenCustomerAccess —
        // a valid dev-login persona that is not on "PlatformAdmin:AllowedEmails" is rejected on the
        // Admin Portal login page itself (Login.razor probes /api/platform-admin/me), never handed
        // a session cookie.
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
        // No login at all — unlike NonAllowListedPersona above (an authenticated caller the
        // server-side "PlatformAdmin:AllowedEmails" check rejects, surfaced as this page's own
        // dashboard-error banner), a genuinely anonymous visitor never gets that far:
        // Routes.razor's AuthorizeRouteView redirects to /login at the router level before
        // FailedPayments.razor — or its API call — ever runs. Navigate directly rather than via
        // FailedPaymentsPage.GoToAsync, which waits for that page's own settled-state selectors
        // and would time out here since none of them exist on /login.
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/failed-payments");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
