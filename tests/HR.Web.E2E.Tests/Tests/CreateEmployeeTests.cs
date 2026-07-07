using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an HR Administrator can create a new employee and that
/// the employee appears in the employee list afterwards.
/// </summary>
[Collection("E2E")]
public sealed class CreateEmployeeTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId        = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid JamesOkaforId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CreateEmployee_WithRequiredFields_AppearsInEmployeeList()
    {
        // Use a unique email so the test can be run more than once on the same database.
        var unique     = Guid.NewGuid().ToString("N")[..8];
        var firstName  = "E2E";
        var lastName   = $"Emp{unique}";
        var workEmail  = $"e2e.emp{unique}@acme.example";
        var startDate  = "01/03/2026";
        var dob        = "15/06/1990";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HR Administrator) ─────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to the employee list ─────────────────────────────
        await empList.GoToAsync(AcmeId);

        // ── Step 3: Click "Add" to navigate to the new-employee form ──────────
        await empList.ClickNewEmployeeAsync();

        // ── Step 4: Fill in required personal information ─────────────────────
        await empEdit.FillFirstNameAsync(firstName);
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);

        // Gender is required — select the first option.
        await empEdit.SelectDropdownAsync("Gender", "Male");

        // Nationality is required.
        await empEdit.SelectDropdownAsync("Nationality", "British");

        // Date of birth.
        await empEdit.FillDateOfBirthAsync(dob);

        // Start date.
        await empEdit.FillStartDateAsync(startDate);

        // ── Step 5: Save the new employee ─────────────────────────────────────
        await empEdit.SaveNewEmployeeAsync();

        // ── Step 6: Back on the employee list — new employee should be present ─
        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear in the employee list after creation");
    }

    [Fact]
    public async Task CreateEmployee_SelectingPositionProfile_PrepopulatesDepartmentAndShowsDefaultsSummary()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        // Department starts unset; selecting a profile with a Department attached should
        // pre-populate it and reveal the read-only "From Position Profile" summary card.
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        Assert.True(await empEdit.HasPositionProfileDefaultsSummaryAsync(),
            "Expected the 'From Position Profile' defaults summary card to appear after selecting a profile");

        var departmentText = await empEdit.GetSelectedDepartmentTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(departmentText),
            "Expected the Department dropdown to be pre-populated from the selected position profile");
    }

    [Fact]
    public async Task CreateEmployee_SelectingPositionProfile_PrepopulatesLocation()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        // "Senior Software Engineer" is seeded with both a Department and a Location
        // ("London Office") — selecting it should unconditionally overwrite both fields.
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        Assert.True(await empEdit.HasPositionProfileDefaultsSummaryAsync(),
            "Expected the 'From Position Profile' defaults summary card to appear after selecting a profile");

        var departmentText = await empEdit.GetSelectedDepartmentTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(departmentText),
            "Expected the Department dropdown to be pre-populated from the selected position profile");

        var locationText = await empEdit.GetSelectedLocationTextAsync();
        Assert.Equal("London Office", locationText);
    }

    [Fact]
    public async Task EmploymentTab_ChangingPositionProfile_UpdatesDepartmentAndLocation()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // Emma Jones starts in Sales / Account Executive with no location assigned, so
        // switching her to "Senior Software Engineer" (Engineering / London Office) produces
        // a visible change in both the Department and Location dropdowns.
        var emmaJonesId = Guid.Parse("30000000-0000-0000-0000-000000000009");

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, emmaJonesId);
        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        var departmentText = await empEdit.GetSelectedDepartmentTextAsync();
        Assert.Equal("Engineering", departmentText);

        var locationText = await empEdit.GetSelectedLocationTextAsync();
        Assert.Equal("London Office", locationText);
    }

    [Fact]
    public async Task EmployeeTasksTab_ClickingTask_OpensTaskDialog_WithoutNavigatingAway()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // Tom Williams has a seeded "Schedule probation review" task assigned to him
        // (mirrors ProfileTasksTabTests, which verifies the same dialog behavior on
        // My Profile's own Tasks tab).
        var tomId = Guid.Parse("30000000-0000-0000-0000-000000000004");

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, tomId);
        await empEdit.OpenTasksTabAsync();

        await _page.WaitForSelectorAsync(".e-grid, .task-cell", new() { Timeout = 15_000 });

        // The View toolbar button starts disabled (e-overlay) until a row is selected.
        await _page.Locator(".e-row").First.ClickAsync();
        await _page.WaitForFunctionAsync(
            "!document.querySelector('[id=\"hr-view\"]')?.classList?.contains('e-overlay')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var urlBeforeClick = _page.Url;
        await _page.Locator("[id='hr-view']").ClickAsync();

        // Should open the task in a dialog (TaskViewDialog), not navigate to /tasks/{id}.
        // Scoped to [role='dialog'] because Syncfusion's SfDialog CssClass propagates onto
        // multiple elements (the outer container, the dialog itself, and the close button),
        // which makes a bare ".task-view-dialog" locator ambiguous under Playwright's strict mode.
        await _page.WaitForSelectorAsync("[role='dialog'].task-view-dialog", new() { Timeout = 15_000 });
        Assert.True(await _page.Locator("[role='dialog'].task-view-dialog").IsVisibleAsync(),
            "Expected clicking View on an employee's Tasks tab to open the task in a dialog");
        Assert.Equal(urlBeforeClick, _page.Url);
    }

    [Fact]
    public async Task Employee_WithManager_HasProbationSummaryOnEmploymentTab()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, JamesOkaforId);
        await empEdit.OpenEmploymentTabAsync();

        Assert.True(await empEdit.HasProbationSummaryAsync(),
            "Expected a probation summary card on the Employment tab for an employee with a manager and a seeded probation record");
    }

    [Fact]
    public async Task CreateEmployee_WithMissingRequiredFields_ShowsValidationErrors()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);

        // Attempt to save without filling anything — should show validation errors.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // Wait for the async save to complete: either an error banner appears or the URL changes.
        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/employees/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // Page must stay on the new-employee form (URL still ends with /new).
        Assert.Contains("/employees/new", _page.Url);

        Assert.True(await empEdit.HasErrorAsync(),
            "Expected validation errors to appear when saving an empty employee form");
    }
}
