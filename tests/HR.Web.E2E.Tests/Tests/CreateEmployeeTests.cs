using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class CreateEmployeeTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId        = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid JamesOkaforId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid TomWilliamsId = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private static readonly Guid BetaCorpId    = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BobTaylorId   = Guid.Parse("30000000-0000-0000-0000-000000000012");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string BetaHrAdminEmail = "grace.kim@betacorp.example";

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
        var unique     = Guid.NewGuid().ToString("N")[..8];
        var firstName  = "E2E";
        var lastName   = $"Emp{unique}";
        var workEmail  = $"e2e.emp{unique}@acme.example";
        var startDate  = "01/03/2026";
        var dob        = "15/06/1990";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);

        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync(firstName);
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);

        await empEdit.SelectDropdownAsync("Gender", "Male");

        await empEdit.SelectDropdownAsync("Nationality", "British");

        await empEdit.FillDateOfBirthAsync(dob);

        await empEdit.FillStartDateAsync(startDate);

        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");

        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.SaveNewEmployeeAsync();

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
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"PosProfile{unique}";
        var workEmail = $"e2e.posprofile{unique}@acme.example";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

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

        await empEdit.SelectDropdownAsync("Location", "London Office");

        await empEdit.SaveNewEmployeeAsync();

        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear in the employee list after creation");

        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(System.Text.RegularExpressions.Regex.Match(
            _page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})").Groups[1].Value);

        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

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

        var tomId = Guid.Parse("30000000-0000-0000-0000-000000000004");

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, tomId);
        await empEdit.OpenTasksTabAsync();

        await _page.WaitForSelectorAsync(".e-grid, .task-cell", new() { Timeout = 15_000 });

        var urlBeforeClick = _page.Url;

        await _page.Locator(".e-row").First.Locator("button[title='View']").ClickAsync();

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

        await empEdit.GoToAsync(AcmeId, JamesOkaforId);

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

        await empEdit.GoToAsync(AcmeId, JamesOkaforId);

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

        await empEdit.GoToAsync(AcmeId, TomWilliamsId);

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

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/employees/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

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

        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await SetBetaCorpNumberingModeAsync(hrSettings, "Manual");

            try
            {
                await empEdit.GoToNewAsync(BetaCorpId);

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


    [Fact]
    public async Task CreateEmployee_WhenCompanyModeIsManual_ShowsRequiredEmployeeNumberInput()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

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

        var unique = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var newEmployeeNumber = $"EMP-{unique}";

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await SetBetaCorpNumberingModeAsync(hrSettings, "Manual");

            try
            {
                await empEdit.GoToAsync(BetaCorpId, BobTaylorId);
                await empEdit.OpenEmploymentTabAsync();

                Assert.True(await empEdit.IsEmployeeNumberInputVisibleAsync(),
                    "Expected the Employee Number field to be editable while Numbering Mode is Manual");

                await empEdit.FillEmployeeNumberAsync(newEmployeeNumber);
                await empEdit.ClickSaveChangesAsync();

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
