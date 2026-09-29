
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AxeCoreDashboardScanTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
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
