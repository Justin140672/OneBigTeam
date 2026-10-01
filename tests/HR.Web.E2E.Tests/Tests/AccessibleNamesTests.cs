using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AccessibleNamesTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
    }

    private async Task OpenAsync(string path)
    {
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/{path}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Theory]
    [InlineData("employees", "Search employees")]
    [InlineData("user-administration", "Search users")]
    [InlineData("reporting", "Search reports")]
    public async Task SearchBox_HasAccessibleName_NotJustPlaceholder(string path, string accessibleName)
    {
        await LoginAsync();
        await OpenAsync(path);

        var search = _page.GetByRole(AriaRole.Textbox, new() { Name = accessibleName });
        await Assertions.Expect(search).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Textbox, new() { Name = accessibleName })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task EmployeeDirectory_SearchAndFilters_HaveAccessibleNames()
    {
        await LoginAsync();
        await OpenAsync("employees/directory");

        await Assertions.Expect(_page.GetByRole(AriaRole.Textbox, new() { Name = "Search the employee directory" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByLabel("Filter by department")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByLabel("Filter by location")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("Department")]
    [InlineData("Location")]
    [InlineData("Employment Type")]
    [InlineData("Status")]
    [InlineData("Saved Views")]
    public async Task HeadcountReportFilters_HaveAccessibleNames(string name)
    {
        await LoginAsync();
        await OpenAsync("reporting/hr-headcount-summary");

        await Assertions.Expect(_page.GetByLabel(name, new() { Exact = true }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task HrSettings_Controls_HaveAccessibleNames_AndRequiredState()
    {
        await LoginAsync();
        await OpenAsync("hr-settings");

        var hours = _page.GetByLabel("Hours Per Day");
        await Assertions.Expect(hours).ToBeVisibleAsync();
        await Assertions.Expect(hours).ToHaveAttributeAsync("aria-required", "true");
        await Assertions.Expect(_page.GetByRole(AriaRole.Group, new() { Name = "Working Week (required)" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EmployeeProfile_Fields_HaveAccessibleNames_AndRequiredState()
    {
        await LoginAsync();
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        await empEdit.GoToViewAsync(AcmeId, SeededE2eEmployees.ProfileViewEditMode.EmployeeId);

        var firstName = _page.GetByRole(AriaRole.Textbox, new() { Name = "First Name", Exact = true });
        await Assertions.Expect(firstName).ToBeVisibleAsync();
        await Assertions.Expect(firstName).ToHaveAttributeAsync("aria-required", "true");
        await Assertions.Expect(_page.GetByRole(AriaRole.Textbox, new() { Name = "Preferred Name", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Header_UserProfileLink_And_Controls_HaveAccessibleNames()
    {
        await LoginAsync();
        await OpenAsync("employees");

        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Search employees" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Log out" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Notifications" })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("employees")]
    [InlineData("user-administration")]
    [InlineData("reporting")]
    [InlineData("reporting/hr-headcount-summary")]
    [InlineData("hr-settings")]
    public async Task AffectedPages_HaveNoSeriousAxeViolations(string path)
    {
        await LoginAsync();
        await OpenAsync(path);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, path);
    }
}
