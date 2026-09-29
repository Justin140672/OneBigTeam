using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ToilBalanceDisplayTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AdminLeaveTab_ShowsAllBalanceSections_IncludingToil()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);

        await empAdmin.OpenLeaveTabAsync();

        Assert.True(
            await _page.Locator(".card-header").Filter(new() { HasText = "Current Balance" }).IsVisibleAsync(),
            "Expected a 'Current Balance' section on the admin Leave tab");

        Assert.True(
            await _page.Locator(".card-header, .card").Filter(new() { HasText = "TOIL" }).First.IsVisibleAsync(),
            "Expected a TOIL balance section on the admin Leave tab");

        var content = await _page.ContentAsync();
        Assert.Contains("Annual Leave", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminLeaveTab_ShowsPendingAndApprovedRequestSections()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, SarahId);

        await empAdmin.OpenLeaveTabAsync();

        var content = await _page.ContentAsync();

        Assert.True(
            await _page.Locator(".card-header").Filter(new() { HasText = "Pending" }).IsVisibleAsync()
            || content.Contains("Pending", StringComparison.OrdinalIgnoreCase),
            "Expected a Pending Requests section");

        Assert.True(
            await _page.Locator(".card-header").Filter(new() { HasText = "Approved" }).IsVisibleAsync()
            || content.Contains("Approved", StringComparison.OrdinalIgnoreCase),
            "Expected an Approved Requests section");
    }
}
