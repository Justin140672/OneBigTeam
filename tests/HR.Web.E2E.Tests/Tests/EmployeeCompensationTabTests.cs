using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeCompensationTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId     = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahChen  = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompensationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Compensation History");
        await EmployeeEditPage.SectionTab(_page, "Compensation History").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Compensation History"),
            "Expected a 'Compensation History' tab on the employee edit page");
        Assert.False(
            await EmployeeEditPage.SectionTab(_page, "Compensation").IsVisibleAsync(),
            "Did not expect a tab labelled exactly 'Compensation' (renamed to 'Compensation History')");
    }

    [Fact]
    public async Task CompensationTab_NoLongerShowsCurrentCompensationCard_ButShowsHistoryGrid()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenCompensationTabAsync();

        Assert.False(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected the 'Current Compensation' panel to have been removed entirely");
        Assert.False(await _page.GetByText("Current Compensation", new() { Exact = true }).IsVisibleAsync(),
            "Did not expect a 'Current Compensation' heading anywhere on the tab");

        Assert.True(await _page.GetByRole(AriaRole.Heading, new() { Name = "Compensation History", Exact = true }).IsVisibleAsync(),
            "Expected the 'Compensation History' card heading");

        var grid = _page.Locator("[data-testid='compensation-history-grid']");
        Assert.True(await grid.IsVisibleAsync(), "Expected the Compensation History grid to be visible");

        var gridText = await grid.TextContentAsync();
        Assert.Contains("145,000.00", gridText);
    }

    [Fact]
    public async Task CompensationTab_ShowsHistoryGrid_WithBothRecords()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenCompensationTabAsync();

        var grid = _page.Locator("[data-testid='compensation-history-grid']");
        Assert.True(await grid.IsVisibleAsync(), "Expected the Compensation History grid to be visible");

        var gridText = await grid.TextContentAsync();
        Assert.Contains("145,000.00", gridText);
        Assert.Contains("120,000.00", gridText);
        Assert.Contains("Annual", gridText);
    }

    [Theory]
    [InlineData("Annual", "52000", "52,000.00")]
    [InlineData("Hourly", "25.5", "25.50")]
    public async Task CompensationTab_ShowsInitialRecord_ForEmployeeCreatedWithSalary(
        string salaryType, string salary, string expectedGridAmount)
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"InitComp{unique}";
        var workEmail = $"e2e.initcomp{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.FillRequiredAddressAsync();
        await empEdit.FillRequiredCompensationAsync(salary, salaryType, "GBP");
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.WaitForDropdownPopulatedAsync("Department");
        await empEdit.WaitForDropdownPopulatedAsync("Location");

        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        await empEdit.OpenCompensationTabAsync();

        Assert.False(await _page.Locator("[data-testid='no-compensation-message']").IsVisibleAsync(),
            "Did not expect the empty-state message: the new-employee form creates the first compensation record");

        var row = empEdit.CompensationHistoryRow("1 Mar 2026");
        Assert.True(await row.First.IsVisibleAsync(),
            "Expected the first compensation record to be effective from the employee's start date");

        var rowText = await row.First.TextContentAsync();
        Assert.Contains(expectedGridAmount, rowText);
        Assert.Contains(salaryType, rowText);
    }

    [Fact]
    public async Task AddEmployee_CompensationCard_DefaultsToAnnualAndGbp()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);

        Assert.True(await empEdit.IsAddEmployeeCompensationCardVisibleAsync(),
            "Expected the Compensation card on the add-employee form");
        Assert.Equal("Annual", await empEdit.GetSalaryTypeTextAsync());
        Assert.Equal("GBP", await empEdit.GetCurrencyValueAsync());
    }

    [Fact]
    public async Task AddEmployee_WithoutSalary_ShowsRequiredError_AndDoesNotCreateEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoSalary{unique}";
        var workEmail = $"e2e.nosalary{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.FillRequiredAddressAsync();
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.ClickSaveButtonAsync();

        Assert.Contains("Please enter a salary.", await empEdit.GetSalaryErrorAsync());
        Assert.Contains("/employees/new", _page.Url);

        await empEdit.FillRequiredCompensationAsync();
        await empEdit.SaveNewEmployeeAsync();

        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear once a salary was entered");
    }

    private async Task<EmployeeEditPage> CreateFreshEmployeeOnCompensationTabAsync(string labelSuffix)
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Comp{labelSuffix}{unique}";
        var workEmail = $"e2e.comp{labelSuffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.FillRequiredAddressAsync();
        await empEdit.FillRequiredCompensationAsync();
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.WaitForDropdownPopulatedAsync("Department");
        await empEdit.WaitForDropdownPopulatedAsync("Location");

        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        await empEdit.OpenCompensationTabAsync();

        return empEdit;
    }

    [Fact]
    public async Task AddCompensation_WithFutureEffectiveDate_AppearsInHistoryWithEditAndDeleteActions()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var empEdit = await CreateFreshEmployeeOnCompensationTabAsync("Add");

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/01/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("38000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        var row = empEdit.CompensationHistoryRow("1 Jan 2030");
        Assert.True(await row.First.IsVisibleAsync(),
            "Expected the newly added future-dated record to appear in the history grid");

        Assert.True(await row.GetByTitle("Edit").IsVisibleAsync(), "Expected an Edit action on the future-dated row");
        Assert.True(await row.GetByTitle("Delete").IsVisibleAsync(), "Expected a Delete action on the future-dated row");
    }

    [Fact]
    public async Task EditFutureCompensation_UpdatesSalary_WithoutChangingEffectiveDate()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SeededE2eEmployees.CompensationEdit.EmployeeId);
        await empEdit.OpenCompensationTabAsync();

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/06/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("40000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        await empEdit.ClickEditCompensationRowAsync("1 Jun 2030");
        await empEdit.FillEditCompensationSalaryAsync("42000");
        await empEdit.SubmitEditCompensationDialogAsync();

        var row = empEdit.CompensationHistoryRow("1 Jun 2030");
        var rowText = await row.First.TextContentAsync();
        Assert.Contains("42,000.00", rowText);
    }

    [Fact]
    public async Task DeleteFutureCompensation_RemovesRecordFromHistory()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var empEdit = await CreateFreshEmployeeOnCompensationTabAsync("Delete");

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/12/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("41000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        await empEdit.ClickDeleteCompensationRowAsync("1 Dec 2030");
        await empEdit.ConfirmDeleteCompensationAsync();

        await Assertions.Expect(empEdit.CompensationHistoryRow("1 Dec 2030").First)
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
