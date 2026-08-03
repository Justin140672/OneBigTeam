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

    // James Okafor: seeded with only a Passed probation record (ProbationModule.SeedProbationAsync's
    // completed-records loop) — no active one, so the tab should be hidden for him.
    private static readonly Guid JamesOkafor  = Guid.Parse("30000000-0000-0000-0000-000000000002");

    // Sarah Chen: CTO with no manager — EmployeeCreatedHandler skips auto-creating a probation
    // record when ManagerId is null, so she never had one at all.
    private static readonly Guid SarahChen    = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ProbationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);

        // The Probation tab item only renders once EmployeeEdit.razor's own LoadAsync sets
        // _showProbationTab from the employee response — GoToAsync's own wait (the Details tab's
        // combobox) can resolve on an earlier render pass than that, before the tab strip has
        // picked it up. Use an auto-retrying assertion rather than a single IsVisibleAsync()
        // snapshot, which has no built-in wait and can catch the page mid-render.
        await Assertions.Expect(_page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
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

    [Fact]
    public async Task ProbationTab_IsHidden_ForEmployeeWithOnlyAPassedRecord()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, JamesOkafor);

        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).IsVisibleAsync(),
            "Expected no 'Probation' tab for an employee whose only probation record is Passed");
    }

    [Fact]
    public async Task ProbationTab_IsHidden_ForEmployeeWhoNeverHadARecord()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).IsVisibleAsync(),
            "Expected no 'Probation' tab for an employee who never had a probation record");
    }
}
