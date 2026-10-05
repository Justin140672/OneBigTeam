using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class InviteUserWizardValidationTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    private async Task<string> CreateFreshUninvitedEmployeeAsync()
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"WizVal{unique}";
        var workEmail = $"e2e.wizval{unique}@acme.example";

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
        await empEdit.SaveNewEmployeeAsync();

        return lastName;
    }

    [Fact]
    public async Task Step1_WithoutEmployee_KeepsWizardOnEmployeeStep_ThenAdvancesOnceSelected()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);
        var wizard = new InviteUserWizardPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var employeeName = await CreateFreshUninvitedEmployeeAsync();

        await list.GoToAsync(AcmeId);
        await wizard.OpenFromToolbarAsync();

        await wizard.ClickNextExpectingNoAdvanceAsync();

        Assert.True(await wizard.IsOpenAsync(), "Expected the Invite User wizard to stay open");
        Assert.Equal("Employee", await wizard.GetActiveStepLabelAsync());
        var error = await wizard.GetFieldValidationMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(error), "Expected an inline validation message on the Employee step");
        Assert.Contains("employee", error, StringComparison.OrdinalIgnoreCase);

        await wizard.SelectEmployeeWithoutAdvancingAsync(employeeName);
        await wizard.ClickNextExpectingAdvanceAsync("Email");
    }

    [Fact]
    public async Task Step2_WithEmptyOrInvalidEmail_KeepsWizardOnEmailStep_ThenAdvancesOnceValid()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);
        var wizard = new InviteUserWizardPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var employeeName = await CreateFreshUninvitedEmployeeAsync();

        await list.GoToAsync(AcmeId);
        await wizard.OpenFromToolbarAsync();

        await wizard.SelectEmployeeWithoutAdvancingAsync(employeeName);
        await wizard.ClickNextExpectingAdvanceAsync("Email");

        await wizard.FillEmailFieldAsync("");
        await wizard.ClickNextExpectingNoAdvanceAsync();
        Assert.Equal("Email", await wizard.GetActiveStepLabelAsync());
        var requiredError = await wizard.GetFieldValidationMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(requiredError), "Expected a required-field message for an empty work email");

        await wizard.FillEmailFieldAsync("not-an-email");
        await wizard.ClickNextExpectingNoAdvanceAsync();
        Assert.Equal("Email", await wizard.GetActiveStepLabelAsync());
        var formatError = await wizard.GetFieldValidationMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(formatError), "Expected an invalid-format message for a malformed work email");
        Assert.Contains("valid", formatError, StringComparison.OrdinalIgnoreCase);

        await wizard.FillEmailFieldAsync($"e2e.wizard.{Guid.NewGuid():N}@acme.example");
        await wizard.ClickNextExpectingAdvanceAsync("Roles");
    }
}
