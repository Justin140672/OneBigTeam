using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AddEmployeeManagerTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string ManagerRequiredMessage = "Select a manager or choose 'No manager — top-level role'.";
    private const string NoManagerWarningText = "This employee won't appear in anyone's reporting line. Use this only for top-level roles.";

    private async Task<(EmployeeListPage List, EmployeeEditPage Edit)> OpenAddEmployeeFormAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        return (empList, empEdit);
    }

    private static async Task FillAllButManagerAsync(EmployeeEditPage empEdit, string lastName, string unique)
    {
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync($"e2e.{lastName.ToLowerInvariant()}@acme.example");
        await empEdit.FillRequiredAddressAsync();
        await empEdit.FillRequiredCompensationAsync();
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");
    }

    private async Task<string> OpenCreatedEmployeeManagerTextAsync(EmployeeListPage empList, EmployeeEditPage empEdit, string lastName)
    {
        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear in the employee list after creation");

        await empList.ClickEmployeeAsync(lastName);
        await empEdit.OpenEmploymentTabAsync();

        return (await empEdit.GetSelectedManagerTextAsync())?.Trim() ?? string.Empty;
    }

    [Fact]
    public async Task AddEmployee_WithoutChoosingManager_ShowsManagerErrorAlert_AndDoesNotSave()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"NoMgrChoice{unique}";

        var (_, empEdit) = await OpenAddEmployeeFormAsync();

        await Assertions.Expect(empEdit.ManagerError).ToHaveCountAsync(0);
        await Assertions.Expect(empEdit.NoManagerWarning).ToHaveCountAsync(0);

        await FillAllButManagerAsync(empEdit, lastName, unique);

        Assert.Equal(string.Empty, await empEdit.GetNewEmployeeManagerTextAsync());

        await empEdit.ClickSaveButtonAsync();

        await Assertions.Expect(empEdit.ManagerError).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(empEdit.ManagerError).ToHaveAttributeAsync("role", "alert");
        await Assertions.Expect(empEdit.ManagerError).ToHaveTextAsync(ManagerRequiredMessage);
        await Assertions.Expect(empEdit.NoManagerWarning).ToHaveCountAsync(0);
        Assert.Contains("/employees/new", _page.Url);
    }

    [Fact]
    public async Task AddEmployee_ChoosingNoManagerTopLevel_ShowsWarning_AndCreatesEmployeeWithoutManager()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"TopLevel{unique}";

        var (empList, empEdit) = await OpenAddEmployeeFormAsync();

        await FillAllButManagerAsync(empEdit, lastName, unique);
        await empEdit.SelectNoManagerTopLevelAsync();

        Assert.Equal(EmployeeEditPage.NoManagerTopLevelOption, await empEdit.GetNewEmployeeManagerTextAsync());
        await Assertions.Expect(empEdit.NoManagerWarning).ToBeVisibleAsync();
        await Assertions.Expect(empEdit.NoManagerWarning).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(empEdit.NoManagerWarning).ToHaveTextAsync(NoManagerWarningText);
        await Assertions.Expect(empEdit.ManagerError).ToHaveCountAsync(0);

        await empEdit.SaveNewEmployeeAsync(selectNoManagerIfUnset: false);

        Assert.Equal("No Manager", await OpenCreatedEmployeeManagerTextAsync(empList, empEdit, lastName));
    }

    [Fact]
    public async Task AddEmployee_ChoosingManager_HidesWarning_AndCreatesEmployeeWithThatManager()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"WithMgr{unique}";

        var (empList, empEdit) = await OpenAddEmployeeFormAsync();

        await FillAllButManagerAsync(empEdit, lastName, unique);
        await empEdit.SelectNewEmployeeManagerAsync("Laura Bennett");

        Assert.Equal("Laura Bennett", await empEdit.GetNewEmployeeManagerTextAsync());
        await Assertions.Expect(empEdit.NoManagerWarning).ToHaveCountAsync(0);
        await Assertions.Expect(empEdit.ManagerError).ToHaveCountAsync(0);

        await empEdit.SaveNewEmployeeAsync(selectNoManagerIfUnset: false);

        Assert.Equal("Laura Bennett", await OpenCreatedEmployeeManagerTextAsync(empList, empEdit, lastName));
    }

    [Fact]
    public async Task AddEmployee_SwitchingFromNoManagerToNamedManager_RemovesWarning()
    {
        var (_, empEdit) = await OpenAddEmployeeFormAsync();

        await empEdit.SelectNoManagerTopLevelAsync();
        await Assertions.Expect(empEdit.NoManagerWarning).ToBeVisibleAsync();

        await empEdit.SelectNewEmployeeManagerAsync("Laura Bennett");
        await Assertions.Expect(empEdit.NoManagerWarning).ToHaveCountAsync(0);
    }
}
