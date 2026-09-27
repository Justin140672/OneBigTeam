using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an HR Administrator can create a new employee and that
/// the employee appears in the employee list afterwards.
///
/// Uses the seeded "QA Engineer" position profile (Engineering / London Office — same Department
/// and Location as "Senior Software Engineer") rather than "Senior Software Engineer" itself for
/// employee creation/assignment. VacancyDetail's "New Vacancy" Position Profile dropdown excludes
/// any profile currently held by an active employee, and many Recruitment E2E tests depend on
/// "Senior Software Engineer" remaining selectable there — since xUnit runs test classes in
/// parallel with no ordering guarantee, assigning employees to "Senior Software Engineer" here
/// would permanently hide it from those unrelated tests for the rest of the run once any test in
/// this class executes.
///
/// Only 3 of this class's methods (the Manual-mode ones below) mutate a shared CompanySettings row
/// (Beta Corp's employee-numbering mode) — the rest just create/view employees against
/// already-seeded, per-test-uniquely-named data and never touch that row. This class therefore
/// runs as an ordinary parallel-eligible class (not class-level HrSettingsSerialTestBase) so those
/// aren't forced to queue behind the whole "HrSettingsSerial" group (HrSettingsPageTests,
/// DataImportWizardTests, etc.) for no reason; the mutating methods
/// acquire HrSettingsSerialTestBase.GateInstance directly around just their mutating/asserting
/// section instead, matching the narrow method-level pattern already used by
/// PositionRoleDefaultsSerialTestBase/SharedProbationGate — see GroupSerializedTestBases.cs.
///
/// Employee-numbering MODE tests never mutate Acme's numbering mode. Acme stays on its seeded
/// Automatic mode for the whole run because dozens of ungated classes create Acme employees
/// concurrently: flipping Acme to Manual mid-run (even inside the HrSettingsSerial gate, which only
/// serializes the mutators against each other) made any of those whose New Employee form had
/// rendered in Automatic mode fail on Save with "Employee number is required.". The tests that
/// genuinely need Manual mode therefore run on Beta Corp as Grace Kim (its HR Administrator), under
/// the same HrSettingsSerial gate HrSettingsPageTests uses for Beta Corp's settings row — nothing
/// else creates Beta Corp employees, so no ungated reader can be caught mid-flip there.
/// </summary>
public sealed class CreateEmployeeTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId        = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid JamesOkaforId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid TomWilliamsId = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private static readonly Guid BetaCorpId    = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BobTaylorId   = Guid.Parse("30000000-0000-0000-0000-000000000012");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string BetaHrAdminEmail = "grace.kim@betacorp.example";

    /// <summary>
    /// Switches Beta Corp to <paramref name="mode"/> (caller must hold HrSettingsSerialTestBase.GateInstance
    /// and be logged in as Grace Kim). Beta Corp's seeded baseline is Automatic; restoring always
    /// targets that known baseline rather than a captured "current" value, so an interrupted run
    /// can't leave the tenant stuck in Manual (same self-healing reasoning as before the move).
    /// </summary>
    private async Task SetBetaCorpNumberingModeAsync(HrSettingsPage hrSettings, string mode)
    {
        await hrSettings.GoToAsync(BetaCorpId);
        await hrSettings.SelectEmployeeNumberModeAsync(mode);
        await hrSettings.SaveAsync();
        Assert.False(await hrSettings.HasErrorAsync(),
            $"Expected no error after switching Beta Corp's numbering mode to {mode}");
        Assert.Equal(mode, await hrSettings.GetEmployeeNumberModeAsync());
    }

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

        // Employee Number and Employment Type are required.
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");

        // Department and Location are required too — selecting a Position Profile that has both
        // attached ("QA Engineer" is seeded with Engineering / London Office)
        // pre-populates them, satisfying all three required fields in one step.
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

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
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

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

        // "QA Engineer" is seeded with both a Department and a Location
        // ("London Office") — selecting it should unconditionally overwrite both fields.
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

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
        // switching her to "QA Engineer" (Engineering / London Office) produces
        // a visible change in both the Department and Location dropdowns.
        var emmaJonesId = Guid.Parse("30000000-0000-0000-0000-000000000009");

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, emmaJonesId);
        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        var departmentText = await empEdit.GetSelectedDepartmentTextAsync();
        Assert.Equal("Engineering", departmentText);

        var locationText = await empEdit.GetSelectedLocationTextAsync();
        Assert.Equal("London Office", locationText);
    }

    [Fact]
    public async Task EmploymentTab_ChangingPositionProfile_PersistsDepartmentAndLocationAfterSave()
    {
        // Uses a freshly-created employee (rather than mutating a shared seeded one like Emma
        // Jones, who other tests — e.g. EmployeeCurrentProfilePhotoTests, ManagerDashboardTests —
        // rely on remaining untouched) so that actually clicking Save here (unlike the sibling
        // EmploymentTab_ChangingPositionProfile_UpdatesDepartmentAndLocation test above, which only
        // verifies the client-side pre-population and never saves) can't leak side effects into
        // other tests.
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"PosProfile{unique}";
        var workEmail = $"e2e.posprofile{unique}@acme.example";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 1: Create an employee starting on "Account Executive" (Sales, no location) ──
        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Account Executive");

        // Account Executive has no Location attached, but CreateEmployee.Validator requires one
        // (RuleFor(r => r.LocationId).NotEmpty()) — without picking one manually here, Save
        // silently fails client-side validation and SaveNewEmployeeAsync's WaitForURLAsync times
        // out, since it doesn't check for the error banner the way ClickSaveChangesAsync does.
        await empEdit.SelectDropdownAsync("Location", "London Office");

        await empEdit.SaveNewEmployeeAsync();

        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear in the employee list after creation");

        // Navigate into the new employee's edit page and read its id back out of the URL
        // (…/employees/{id}/view — EmployeeList.razor's row link/OnRecordClick lands on the view
        // route) rather than adding a separate id-lookup helper. A trailing-segment split would
        // grab "view" instead of the id, so match the guid directly instead.
        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(System.Text.RegularExpressions.Regex.Match(
            _page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})").Groups[1].Value);

        // ── Step 2: Open the Employment tab and change Position Profile ────────
        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        // Changing Position Profile auto-populates Department/Location from the newly-selected
        // profile via a server round-trip — reading the Department field immediately can race that
        // update and observe the previous profile's ("Account Executive" -> "Sales") stale value.
        // Poll for the expected post-change value rather than asserting instantly.
        string? departmentTextBeforeSave = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            departmentTextBeforeSave = await empEdit.GetSelectedDepartmentTextAsync();
            if (departmentTextBeforeSave == "Engineering") break;
            await _page.WaitForTimeoutAsync(250);
        }
        Assert.Equal("Engineering", departmentTextBeforeSave);

        var locationTextBeforeSave = await empEdit.GetSelectedLocationTextAsync();
        Assert.Equal("London Office", locationTextBeforeSave);

        // ── Step 3: Save and reload — the new Department/Location must persist ─
        await empEdit.ClickSaveChangesAsync();

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenEmploymentTabAsync();

        var departmentTextAfterReload = await empEdit.GetSelectedDepartmentTextAsync();
        Assert.Equal("Engineering", departmentTextAfterReload);

        var locationTextAfterReload = await empEdit.GetSelectedLocationTextAsync();
        Assert.Equal("London Office", locationTextAfterReload);
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

        var urlBeforeClick = _page.Url;

        // The "View" action is a button directly on the row (TaskList.razor) — there's no grid
        // toolbar here, so no row selection is needed; matches ProfileTasksTabTests' equivalent
        // test for My Profile's own Tasks tab.
        await _page.Locator(".e-row").First.Locator("button[title='View']").ClickAsync();

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
    public async Task Employee_WithManager_ShowsReportsToOnOverview()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // James Okafor is seeded reporting to Sarah Chen (EmployeesModule.SeedEmployeesAsync).
        await empEdit.GoToAsync(AcmeId, JamesOkaforId);

        // The Reporting Chain summary is a separate async-loaded panel — GoToAsync's own wait
        // condition (the Details tab's combobox) doesn't guarantee it has rendered yet, so reading
        // page content immediately after can race it (in the worst case even catching the
        // pre-hydration HTML shell, same reasoning as Employee_WithDirectReports_ShowsDirectReportsCountOnOverview
        // below). Wait for a concrete signal from that panel first.
        await _page.GetByText("Reports To:").WaitForAsync(new() { Timeout = 15_000 });

        var content = await _page.ContentAsync();
        Assert.Contains("Reports To:", content);
        Assert.Contains("Sarah Chen", content);
    }

    [Fact]
    public async Task Employee_WithDirectReports_ShowsDirectReportsCountOnOverview()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // James Okafor is seeded as Tom Williams's manager (EmployeesModule.SeedEmployeesAsync).
        await empEdit.GoToAsync(AcmeId, JamesOkaforId);

        // The Reporting Chain/Direct Reports summary is a separate async-loaded panel — GoToAsync's
        // own wait condition (the Details tab's combobox) doesn't guarantee it has rendered yet, so
        // reading page content immediately after can race it (in the worst case even catching the
        // pre-hydration HTML shell). Wait for a concrete signal from that panel first.
        await _page.GetByText("Direct Reports:").WaitForAsync(new() { Timeout = 15_000 });

        var content = await _page.ContentAsync();
        Assert.Contains("Direct Reports:", content);
        Assert.Contains("1 Employee", content);
    }

    [Fact]
    public async Task Employee_WithManagerChain_ShowsReportingChainOnOverview()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Tom Williams reports to James Okafor, who reports to Sarah Chen (no manager).
        await empEdit.GoToAsync(AcmeId, TomWilliamsId);

        // Same reasoning as Employee_WithDirectReports_ShowsDirectReportsCountOnOverview — wait
        // for the async-loaded Reporting Chain panel's own heading before reading page content.
        await _page.GetByRole(AriaRole.Heading, new() { Name = "Reporting Chain" }).WaitForAsync(new() { Timeout = 15_000 });

        var content = await _page.ContentAsync();
        Assert.Contains("Reporting Chain", content);
        Assert.Contains("Sarah Chen", content);
        Assert.Contains("James Okafor", content);
        Assert.Contains("Current Employee", content);
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

    [Fact]
    public async Task CreateEmployee_MissingEmployeeNumber_ShowsValidationError_AndDoesNotCreateEmployee()
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoEmpNum{unique}";
        var workEmail = $"e2e.noempnum{unique}@betacorp.example";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        // Employee Number only renders (and is only required) in Manual numbering mode — see
        // EmployeeEdit.razor's field. Beta Corp, not Acme: see this class's remarks.
        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await SetBetaCorpNumberingModeAsync(hrSettings, "Manual");

            try
            {
                await empEdit.GoToNewAsync(BetaCorpId);

                // Fill every required field except Employee Number. "Software Developer" is Beta
                // Corp's seeded profile carrying both a Department and a Location (Engineering /
                // Leeds Office), so those two auto-populate from it.
                await empEdit.FillFirstNameAsync("E2E");
                await empEdit.FillLastNameAsync(lastName);
                await empEdit.FillWorkEmailAsync(workEmail);
                await empEdit.SelectDropdownAsync("Gender", "Male");
                await empEdit.SelectDropdownAsync("Nationality", "British");
                await empEdit.FillDateOfBirthAsync("15/06/1990");
                await empEdit.FillStartDateAsync("01/03/2026");
                await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
                await empEdit.SelectDropdownAsync("Position Profile", "Software Developer");

                await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

                await _page.WaitForSelectorAsync(".validation-message", new() { Timeout = 15_000 });

                Assert.Contains("/employees/new", _page.Url);
                Assert.True(await empEdit.HasValidationMessageAsync("Employee number is required."),
                    "Expected a validation message indicating Employee Number is required");
            }
            finally
            {
                await SetBetaCorpNumberingModeAsync(hrSettings, "Automatic");
            }
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task CreateEmployee_MissingEmploymentType_ShowsValidationError_AndDoesNotCreateEmployee()
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoEmpType{unique}";
        var workEmail = $"e2e.noemptype{unique}@acme.example";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);

        // Fill every required field except Employment Type.
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForSelectorAsync(".validation-message", new() { Timeout = 15_000 });

        Assert.Contains("/employees/new", _page.Url);
        Assert.True(await empEdit.HasValidationMessageAsync("Employment type is required."),
            "Expected a validation message indicating Employment Type is required");
    }

    [Fact]
    public async Task CreateEmployee_MissingDepartmentLocationAndPositionProfile_ShowsValidationErrors_AndDoesNotCreateEmployee()
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoDeptLocProf{unique}";
        var workEmail = $"e2e.nodeptlocprof{unique}@acme.example";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);

        // Fill every required field except Department, Location and Position Profile — leaving
        // all three dropdowns unset (no Position Profile is selected, so none of the three get
        // auto-populated by the profile-defaults cascade).
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForSelectorAsync(".validation-message", new() { Timeout = 15_000 });

        Assert.Contains("/employees/new", _page.Url);
        Assert.True(await empEdit.HasValidationMessageAsync("Department is required."),
            "Expected a validation message indicating Department is required");
        Assert.True(await empEdit.HasValidationMessageAsync("Location is required."),
            "Expected a validation message indicating Location is required");
        Assert.True(await empEdit.HasValidationMessageAsync("Position profile is required."),
            "Expected a validation message indicating Position Profile is required");
    }

    // ── Employee Numbering (Wave 2) ──────────────────────────────────────────────

    [Fact]
    public async Task CreateEmployee_WhenCompanyModeIsManual_ShowsRequiredEmployeeNumberInput()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        // Mutates Beta Corp's shared CompanySettings row (employee-numbering mode), which
        // HrSettingsSerialTestBase's group (HrSettingsPageTests etc.) also reads/writes — serialize
        // against that group for just this section (see this class's remarks for why Beta Corp).
        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await SetBetaCorpNumberingModeAsync(hrSettings, "Manual");

            try
            {
                await empEdit.GoToNewAsync(BetaCorpId);

                Assert.True(await empEdit.IsEmployeeNumberInputVisibleAsync(),
                    "Expected the Employee Number text input to be visible when the company's numbering mode is Manual");
                Assert.False(await empEdit.HasEmployeeNumberAutoAssignedMessageAsync(),
                    "Did not expect the auto-assigned informational message while in Manual mode");
            }
            finally
            {
                await SetBetaCorpNumberingModeAsync(hrSettings, "Automatic");
            }
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task CreateEmployee_WhenCompanyModeIsAutomatic_HidesEmployeeNumberInput_And_AssignsNumberOnSave()
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"AutoNum{unique}";
        var workEmail = $"e2e.autonum{unique}@acme.example";

        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var hrSettings  = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Acme runs on its seeded Automatic mode for the whole E2E run and no test mutates it any
        // more (see this class's remarks) — so this reads, rather than sets, the mode. Confirm the
        // precondition explicitly so a future regression that flips Acme fails here, clearly.
        await hrSettings.GoToAsync(AcmeId);
        Assert.Equal("Automatic", await hrSettings.GetEmployeeNumberModeAsync());

        await empEdit.GoToNewAsync(AcmeId);

        Assert.False(await empEdit.IsEmployeeNumberInputVisibleAsync(),
            "Did not expect the Employee Number text input to be visible when the company's numbering mode is Automatic");
        Assert.True(await empEdit.HasEmployeeNumberAutoAssignedMessageAsync(),
            "Expected the auto-assigned informational message while in Automatic mode");

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.SaveNewEmployeeAsync();

        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear in the employee list after creation");
    }

    [Fact]
    public async Task EmploymentTab_EditingEmployeeNumber_PersistsNewValue()
    {
        var login      = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit    = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        // Uppercase — UpdateEmploymentDetailsHandler normalizes employee numbers to uppercase
        // server-side (see Employee.NormalizeEmployeeNumber), and Guid.NewGuid()'s hex digits are
        // lowercase, which would otherwise mismatch the persisted/displayed value.
        var unique = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var newEmployeeNumber = $"EMP-{unique}";

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        // UpdateEmploymentDetailsHandler only lets HR change an employee number while the company
        // is in Manual mode (Automatic numbers are system-generated and read-only), so this test
        // needs Manual mode — on Beta Corp, not Acme (see this class's remarks). This doubles as
        // the E2E coverage that Manual mode still lets an admin set employee numbers by hand.
        // SetBetaCorpNumberingModeAsync's restore targets Beta Corp's known seeded baseline
        // ("Automatic") rather than a captured value, so an interrupted run self-heals.
        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await SetBetaCorpNumberingModeAsync(hrSettings, "Manual");

            try
            {
                // Bob Taylor is a pre-seeded Beta Corp employee — edit his Employment tab's number.
                await empEdit.GoToAsync(BetaCorpId, BobTaylorId);
                await empEdit.OpenEmploymentTabAsync();

                Assert.True(await empEdit.IsEmployeeNumberInputVisibleAsync(),
                    "Expected the Employee Number field to be editable while Numbering Mode is Manual");

                await empEdit.FillEmployeeNumberAsync(newEmployeeNumber);
                await empEdit.ClickSaveChangesAsync();

                // Re-navigate to confirm the new value persisted and shows in the header badge.
                await empEdit.GoToAsync(BetaCorpId, BobTaylorId);
                Assert.Equal($"#{newEmployeeNumber}", await empEdit.GetEmployeeNumberHeaderTextAsync());
            }
            finally
            {
                await SetBetaCorpNumberingModeAsync(hrSettings, "Automatic");
            }
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }
    }
}
