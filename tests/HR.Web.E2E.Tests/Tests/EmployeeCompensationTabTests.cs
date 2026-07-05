using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Compensation tab on the employee edit page.
///
/// Uses the seeded "Sarah Chen" employee (ID: 30000000-0000-0000-0000-000000000001) who has a
/// seeded Annual compensation record: 145,000 GBP effective 6 Jan 2020, 37.5 hrs/week, 1.0 FTE.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeCompensationTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId     = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahChen  = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompensationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Compensation" }).IsVisibleAsync(),
            "Expected a 'Compensation' tab on the employee edit page");
    }

    [Fact]
    public async Task CompensationTab_ShowsCurrentCompensationPanel_WithExpectedFields()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenCompensationTabAsync();

        Assert.True(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected the Current Compensation panel to be visible");

        var salary = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains("145,000.00", salary);
        Assert.Contains("per year", salary);

        var annualisedSalary = await empEdit.GetCompensationFieldTextAsync("compensation-annualised-salary");
        Assert.Contains("145,000.00", annualisedSalary);

        var hours = await empEdit.GetCompensationFieldTextAsync("compensation-hours");
        Assert.Contains("37.5", hours);

        var fte = await empEdit.GetCompensationFieldTextAsync("compensation-fte");
        Assert.Contains("100", fte);

        var effectiveFrom = await empEdit.GetCompensationFieldTextAsync("compensation-effective-from");
        Assert.Contains("2020", effectiveFrom);
    }

    [Fact]
    public async Task CompensationTab_ShowsEmptyState_ForEmployeeWithNoCompensationRecord()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Tom Williams has no seeded compensation record.
        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenCompensationTabAsync();

        Assert.False(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected no Current Compensation panel for an employee without a compensation record");

        Assert.True(await _page.Locator(".alert-secondary").IsVisibleAsync(),
            "Expected an empty-state message when no compensation record exists");
    }
}
