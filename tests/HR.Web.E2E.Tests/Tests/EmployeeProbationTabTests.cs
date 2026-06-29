using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Probation tab on the employee edit page.
///
/// Uses the seeded "Carlos Rivera" employee (ID: 30000000-0000-0000-0000-000000000010)
/// who has an active probation record with a pending ManagerCheckIn review.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeProbationTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId       = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CarlosRivera = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ProbationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).IsVisibleAsync(),
            "Expected a 'Probation' tab on the employee edit page");
    }

    [Fact]
    public async Task ProbationTab_ShowsProbationPeriodSummaryPanel()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        Assert.True(await empEdit.HasProbationPeriodSummaryPanelAsync(),
            "Expected the probation period summary panel (progress bar) to be visible");
    }

    [Fact]
    public async Task ProbationTab_ShowsReviewHistoryGrid()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        Assert.True(await empEdit.HasProbationReviewsGridAsync(),
            "Expected the Syncfusion review history grid to be visible on the Probation tab");
    }

    [Fact]
    public async Task ProbationTab_ShowsActiveOrReviewDueStatus()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        var status = await empEdit.GetProbationStatusBadgeTextAsync();
        Assert.True(
            status is "Active" or "Review Due" or "Extended",
            $"Expected an in-progress probation status, got '{status}'");
    }
}
