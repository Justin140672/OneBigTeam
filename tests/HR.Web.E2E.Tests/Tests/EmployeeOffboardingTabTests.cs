using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Offboarding tab on the employee edit page (empty state, the "Start Offboarding"
/// dialog and its validation, the resulting progress panel / checklist, and deep-link tab
/// activation).
///
/// Unlike Onboarding (auto-created via a domain event when an employee is created), an
/// Offboarding plan is only ever created by explicitly submitting the "Start Offboarding"
/// dialog — there is no seed data or auto-provisioning path. Every test below therefore creates
/// a fresh employee through the standard New Employee form (mirroring
/// EmployeeOnboardingTabTests.cs's CreateEmployeeWithFreshOnboardingPlanAsync), which reliably
/// has zero assigned assets and no offboarding plan yet.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeOffboardingTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form and returns their id,
    /// captured from the URL after navigating back into their profile from the employee list.
    /// Caller must already be logged in as an HR administrator. The employee has no assigned
    /// assets and no offboarding plan yet.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Offboard{suffix}{unique}";
        var workEmail = $"e2e.offboard.{suffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");

        // Employee Number, Employment Type, Department, Location and Position Profile are all
        // mandatory now. Selecting "Senior Software Engineer" (seeded with Engineering / London
        // Office attached) pre-populates Department and Location in one step — same pattern as
        // CreateEmployeeTests.cs.
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task OffboardingTab_IsVisible_OnNewlyCreatedEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Vis");

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected an 'Offboarding' tab on the employee edit page");
    }

    [Fact]
    public async Task OffboardingTab_ShowsEmptyState_ForFreshEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var offboarding = new EmployeeOffboardingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Empty");
        await offboarding.OpenAsync();

        Assert.True(await offboarding.IsEmptyStateVisibleAsync(),
            "Expected the 'No offboarding plan found for this employee' empty state to be visible");
        Assert.True(await offboarding.HasStartOffboardingButtonAsync(),
            "Expected the 'Start Offboarding' button to be visible");
    }

    [Fact]
    public async Task StartOffboarding_WithValidLastWorkingDay_CreatesPlanAndShowsOverview()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var offboarding = new EmployeeOffboardingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Create");
        await offboarding.OpenAsync();

        await offboarding.OpenStartDialogAsync();
        await offboarding.FillLastWorkingDayAsync("01/12/2026");
        await offboarding.FillNotesAsync("Standard exit process — E2E test.");
        await offboarding.SubmitStartAsync();

        Assert.False(await offboarding.IsStartDialogVisibleAsync(),
            "Expected the Start Offboarding dialog to close after a successful submission");
        Assert.True(await offboarding.HasProgressPanelAsync(),
            "Expected the offboarding progress panel to be visible after starting a plan");
        Assert.True(await offboarding.HasChecklistCardAsync(),
            "Expected the Offboarding Checklist card to be visible after starting a plan");

        var status = await offboarding.GetStatusBadgeTextAsync();
        Assert.True(
            status is "Not Started" or "In Progress",
            $"Expected a sensible newly-started offboarding plan status, got '{status}'");
    }

    [Fact]
    public async Task StartOffboarding_WithoutLastWorkingDay_ShowsValidationErrorAndKeepsDialogOpen()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var offboarding = new EmployeeOffboardingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Valid");
        await offboarding.OpenAsync();

        await offboarding.OpenStartDialogAsync();
        // Deliberately leave "Last Working Day" empty and submit straight away.
        await offboarding.SubmitStartAsync();

        Assert.True(await offboarding.IsStartDialogVisibleAsync(),
            "Expected the Start Offboarding dialog to stay open when Last Working Day is missing");

        var error = await offboarding.GetStartDialogErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Start Offboarding dialog");
        Assert.Contains("last working day", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeepLink_TabOffboarding_LandsDirectlyOnOffboardingTab()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var employee = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, "Deep");

        // EmployeeEdit.razor's LoadAsync maps "?tab=offboarding" to tab index 12 (the last tab).
        await empEdit.GoToAsync(AcmeId, employeeId, "tab=offboarding");

        Assert.Equal("Offboarding", await employee.GetActiveTabNameAsync());
    }

    [Fact]
    public async Task StartOffboarding_ForEmployeeWithNoAssets_GeneratesExpectedFixedChecklistTasks()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var offboarding = new EmployeeOffboardingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // A freshly created employee has zero assigned assets, so StartOffboardingHandler
        // should generate exactly 5 tasks: 1 HR document-review task + 4 fixed manager
        // exit-checklist tasks (see StartOffboardingHandler.CreateDocumentReviewTaskAsync /
        // CreateManagerExitChecklistAsync).
        await CreateEmployeeAsync(empList, empEdit, "Tasks");
        await offboarding.OpenAsync();

        await offboarding.OpenStartDialogAsync();
        await offboarding.FillLastWorkingDayAsync("15/01/2027");
        await offboarding.SubmitStartAsync();

        Assert.True(await offboarding.HasChecklistCardAsync(),
            "Expected the Offboarding Checklist card to be visible after starting a plan");

        // Fixed HR task title (StartOffboardingHandler.CreateDocumentReviewTaskAsync) — no
        // employee name interpolated, so the full title can be matched exactly.
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Review outstanding documents for employee exit"),
            "Expected the fixed HR document-review task to appear in the checklist");

        // Fixed manager exit-checklist task titles interpolate the employee's display name
        // (e.g. "Conduct exit interview — E2E OffboardTasks<unique>"), so match on the
        // stable title prefix only (StartOffboardingHandler.CreateManagerExitChecklistAsync).
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Conduct exit interview"),
            "Expected the fixed exit-interview task to appear in the checklist");
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Revoke system access and accounts"),
            "Expected the fixed access-revocation task to appear in the checklist");
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Arrange handover and knowledge transfer"),
            "Expected the fixed handover task to appear in the checklist");
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Notify IT and Payroll of employee exit"),
            "Expected the fixed IT/Payroll notification task to appear in the checklist");
    }
}
