using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Sickness tab on the employee edit page: the record history grid,
/// recording a new sickness absence via the "Record Sickness" dialog, and closing
/// an open record via the "Close" dialog.
///
/// Uses the seeded "Tom Williams" employee (ID: 30000000-0000-0000-0000-000000000004)
/// as the target employee, with Laura Bennett (HR Administrator) performing the actions.
/// A sickness category is created fresh in each test that needs one, since categories
/// are not seeded by default (see SicknessCategoryManagementTests.cs for the same pattern).
/// </summary>
[Collection("E2E")]
public sealed class EmployeeSicknessTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task SicknessTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);

        // GoToAsync only waits for a combobox to render, not for the full tab list — which
        // depends on the employee's own async-loaded data (_showProbationTab etc.) — so a bare
        // instant IsVisibleAsync() here can race that and report "not visible" for a tab that's
        // genuinely there a moment later. A bounded wait avoids that.
        await _page.GetByRole(AriaRole.Tab, new() { Name = "Sickness" }).WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Sickness" }).IsVisibleAsync(),
            "Expected a 'Sickness' tab on the employee edit page");
    }

    [Fact]
    public async Task SicknessTab_ShowsRecordHistoryGrid()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenSicknessTabAsync();

        Assert.True(await empEdit.HasSicknessGridAsync(),
            "Expected the Syncfusion sickness record history grid to be visible on the Sickness tab");
    }

    [Fact]
    public async Task RecordSickness_AppearsInGrid_WithActiveStatus()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var catName  = $"E2E Cold {suffix}";
        // A distinctive, unlikely-to-collide start date so the grid row can be found reliably.
        var startDate = new DateOnly(2026, 1, 15).AddDays(Random.Shared.Next(0, 300));
        var startDateGridText = startDate.ToString("dd MMM yyyy");

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create a category to select in the Record Sickness dialog.
        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);
        await catEdit.FillDisplayOrderAsync(1);
        await catEdit.SaveAsync();

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenSicknessTabAsync();

        await empEdit.OpenRecordSicknessDialogAsync();
        await empEdit.SelectRecordSicknessCategoryAsync(catName);
        await empEdit.FillRecordSicknessStartDateAsync(startDate.ToString("dd/MM/yyyy"));
        await empEdit.SubmitRecordSicknessAsync();

        Assert.False(await empEdit.HasRecordSicknessErrorAsync(),
            "Expected no error after recording a new sickness absence");

        var status = await empEdit.GetSicknessStatusBadgeForStartDateAsync(startDateGridText);
        Assert.Equal("Active", status);

        // Close the record so this employee is left with no open sickness record —
        // RecordSickness rejects a second open record for the same employee, and other
        // tests/re-runs sharing this seeded employee would otherwise fail.
        await empEdit.StartCloseSicknessRecordAsync(startDateGridText);
        await empEdit.FillCloseSicknessEndDateAsync(startDate.AddDays(1).ToString("dd/MM/yyyy"));
        await empEdit.SubmitCloseSicknessRecordAsync();
    }

    [Fact]
    public async Task CloseSicknessRecord_UpdatesStatus_ToClosed()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var catName  = $"E2E Flu {suffix}";
        var startDate = new DateOnly(2026, 1, 15).AddDays(Random.Shared.Next(300, 600));
        var startDateGridText = startDate.ToString("dd MMM yyyy");
        var endDate = startDate.AddDays(3);

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);
        await catEdit.FillDisplayOrderAsync(1);
        await catEdit.SaveAsync();

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenSicknessTabAsync();

        // Record an open (no end date) sickness absence first.
        await empEdit.OpenRecordSicknessDialogAsync();
        await empEdit.SelectRecordSicknessCategoryAsync(catName);
        await empEdit.FillRecordSicknessStartDateAsync(startDate.ToString("dd/MM/yyyy"));
        await empEdit.SubmitRecordSicknessAsync();

        var openStatus = await empEdit.GetSicknessStatusBadgeForStartDateAsync(startDateGridText);
        Assert.Equal("Active", openStatus);

        // Now close it.
        await empEdit.StartCloseSicknessRecordAsync(startDateGridText);
        await empEdit.FillCloseSicknessEndDateAsync(endDate.ToString("dd/MM/yyyy"));
        await empEdit.SubmitCloseSicknessRecordAsync();

        var closedStatus = await empEdit.GetSicknessStatusBadgeForStartDateAsync(startDateGridText);
        Assert.Equal("Closed", closedStatus);
    }
}
