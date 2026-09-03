using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers HR.Admin.Web's read-only Customer Support view (/customers/{CompanyId}/support —
/// CustomerSupportView.razor):
/// - An allow-listed platform admin sees the condensed troubleshooting summary for the seeded
///   Acme tenant (subscription status, stat cards, subscription/trial and users/employees panels),
///   and a "Back to customer details" link.
/// - An unknown company id resolves to the dashboard-error banner, not a crash.
/// - A valid dev-login persona not on "PlatformAdmin:AllowedEmails" is rejected server-side and
///   sees the dashboard-error banner, not support data.
/// - A genuinely anonymous visitor is redirected to /login.
///
/// This page is view-only by design — no create/edit/delete affordance.
/// </summary>
public sealed class CustomerSupportViewTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string AllowListedAdminEmail = "priya.shah@acme.example";
    private const string NonAllowListedEmail = "tom.williams@acme.example";

    [Fact]
    public async Task AllowListedAdmin_SeesSupportSummaryForSeededTenant()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var support = new CustomerSupportViewPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await support.GoToAsync(AcmeId);

        Assert.False(await support.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see the support view, not the error banner");
        Assert.True(await support.IsDetailsGridVisibleAsync());

        Assert.Equal("Acme Corporation", await support.GetCompanyNameAsync());
        Assert.Equal("Active", await support.GetSubscriptionStatusAsync());

        Assert.False(string.IsNullOrWhiteSpace(await support.GetStatCardValueAsync("Portal users")));
        Assert.False(string.IsNullOrWhiteSpace(await support.GetStatCardValueAsync("Subscription status")));

        Assert.True(await support.BackToCustomerDetailsLink.IsVisibleAsync());

        // Recent invoices panel must resolve to exactly one of its states, never both.
        var empty = await support.IsRecentInvoicesEmptyStateVisibleAsync();
        var table = await support.IsRecentInvoicesTableVisibleAsync();
        Assert.True(empty ^ table, "Recent invoices panel should show either the empty state or the snapshot table");
    }

    [Fact]
    public async Task UnknownCompanyId_ShowsErrorBanner_NotCrash()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var support = new CustomerSupportViewPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await support.GoToAsync(Guid.NewGuid());

        Assert.True(await support.IsErrorBannerVisibleAsync(),
            "Expected the error banner for an unknown company id, not a crash or blank page");
        Assert.False(await support.IsDetailsGridVisibleAsync());
    }

    [Fact]
    public async Task NonAllowListedPersona_SeesErrorBanner_NotSupportData()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var support = new CustomerSupportViewPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NonAllowListedEmail);

        await support.GoToAsync(AcmeId);

        Assert.True(await support.IsErrorBannerVisibleAsync(),
            "Expected a non-allow-listed persona to see the unauthorised error banner");
        Assert.False(await support.IsDetailsGridVisibleAsync(),
            "No support summary should render for a non-allow-listed persona");
    }

    [Fact]
    public async Task AnonymousAccess_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/customers/{AcmeId}/support");
        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
