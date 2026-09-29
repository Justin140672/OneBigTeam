using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeEmploymentTabNoticePeriodOverrideTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, int slot)
    {
        _ = empList;
        var seeded = SeededE2eEmployees.NoticePeriodOverride[slot];
        await empEdit.GoToAsync(companyId: AcmeId, employeeId: seeded.EmployeeId);
        return (seeded.EmployeeId, seeded.LastName);
    }

    [Fact]
    public async Task NewEmployee_NoticePeriodOverride_IsUncheckedByDefault_AndSourceIsCompanyDefault()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit, slot: 0);
        await empEdit.OpenEmploymentTabAsync();

        Assert.False(await empEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the 'Override notice period' checkbox to be unchecked by default");
        Assert.False(await empEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to stay hidden while the override is unchecked");

        Assert.Equal("Company Default", await empEdit.GetNoticeSourceLabelAsync());
        Assert.Equal("1 Months", await empEdit.GetEffectiveNoticePeriodTextAsync());
    }

    [Fact]
    public async Task SetNoticePeriodOverride_PersistsUnitLengthAndSource_AcrossReload()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit, slot: 1);
        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SetOverrideNoticePeriodAsync(true);
        await empEdit.SelectNoticePeriodUnitAsync("Weeks");
        await empEdit.FillNoticePeriodLengthAsync(3);

        await empEdit.ClickSaveChangesAsync();

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenEmploymentTabAsync();

        Assert.True(await empEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the 'Override notice period' checkbox to be checked after reload");
        Assert.True(await empEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to be visible after reload");
        Assert.Equal("Weeks", await empEdit.GetNoticePeriodUnitTextAsync());
        Assert.Equal(3, await empEdit.GetNoticePeriodLengthAsync());

        Assert.Equal("Employee", await empEdit.GetNoticeSourceLabelAsync());
        Assert.Equal("3 Weeks", await empEdit.GetEffectiveNoticePeriodTextAsync());
    }

    [Fact]
    public async Task TurnOffNoticePeriodOverride_FallsBackToCompanyDefault_AndHidesFieldsAcrossReload()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit, slot: 2);

        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SetOverrideNoticePeriodAsync(true);
        await empEdit.SelectNoticePeriodUnitAsync("Months");
        await empEdit.FillNoticePeriodLengthAsync(2);

        await empEdit.ClickSaveChangesAsync();

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.True(await empEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the 'Override notice period' checkbox to be checked before editing it off");
        Assert.Equal("Employee", await empEdit.GetNoticeSourceLabelAsync());

        await empEdit.SetOverrideNoticePeriodAsync(false);
        await empEdit.ClickSaveChangesAsync();

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenEmploymentTabAsync();

        Assert.False(await empEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the 'Override notice period' checkbox to be unchecked after saving it off");
        Assert.False(await empEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to be hidden after the override was turned off and reloaded");

        Assert.Equal("Company Default", await empEdit.GetNoticeSourceLabelAsync());
        Assert.Equal("1 Months", await empEdit.GetEffectiveNoticePeriodTextAsync());
    }
}
