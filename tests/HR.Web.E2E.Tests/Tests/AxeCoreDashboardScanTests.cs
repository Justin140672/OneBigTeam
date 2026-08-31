// DSH-07 introduced this axe-core scan for the three operational dashboards. NFR-05 generalised the
// inline axe block into the reusable AccessibilityScan helper (Infrastructure/AccessibilityScan.cs)
// and applies the same gate across many more journeys — this class keeps its original 3 dashboard
// theory cases and now just delegates to that shared helper.

using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Runs an axe-core accessibility scan against /dashboard/hr, /dashboard/manager and
/// /dashboard/recruitment and fails on any serious/critical WCAG violation. Compile-only in this
/// repo's CI, like the rest of HR.Web.E2E.Tests.
/// </summary>
public sealed class AxeCoreDashboardScanTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    [Theory]
    [InlineData("laura.bennett@acme.example", "/dashboard/hr")]
    [InlineData("james.okafor@acme.example", "/dashboard/manager")]
    [InlineData("marcus.diallo@acme.example", "/dashboard/recruitment")]
    public async Task Dashboard_HasNoSeriousOrCriticalAxeViolations(string email, string route)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}{route}");
        await _page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, $"dashboard {route}");
    }
}
