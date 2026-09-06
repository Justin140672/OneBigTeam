using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Locks in the declarative &lt;EditForm&gt; + DataAnnotationsValidator refactor of the 4-step
/// InviteUserWizard.razor (Employee → Email → Roles → Review): each step's required/invalid
/// field keeps the wizard on that step with an inline &lt;ValidationMessage&gt;, and correcting it
/// lets the wizard advance.
///
/// Complements <see cref="InviteUserFromAdminTests"/> (happy-path send) and
/// <see cref="UserAdministrationManagementTests"/>. Uses Laura Bennett (HR Administrator) against
/// the seeded Acme company and a freshly-created, uniquely-named employee per test as the invite
/// target — never a shared seeded "no account" employee (Emma Jones / Sophie Laurent), since the
/// other invite tests consume those by actually sending an invitation, which would remove them
/// from this wizard's invitable list under parallel execution. These tests never click
/// "Send invitation", so no invitation is created and no shared state is mutated.
/// </summary>
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
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");
        await empEdit.SaveNewEmployeeAsync();

        // Return the unique last name only — it's a substring of the wizard dropdown's rendered
        // Name regardless of first/last ordering, and filters the invitable list to exactly one.
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

        // Try to advance past step 1 without picking an employee.
        await wizard.ClickNextExpectingNoAdvanceAsync();

        Assert.True(await wizard.IsOpenAsync(), "Expected the Invite User wizard to stay open");
        Assert.Equal("Employee", await wizard.GetActiveStepLabelAsync());
        var error = await wizard.GetFieldValidationMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(error), "Expected an inline validation message on the Employee step");
        Assert.Contains("employee", error, StringComparison.OrdinalIgnoreCase);

        // Selecting an employee now lets the wizard advance to the Email step.
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

        // Empty work email — required.
        await wizard.FillEmailFieldAsync("");
        await wizard.ClickNextExpectingNoAdvanceAsync();
        Assert.Equal("Email", await wizard.GetActiveStepLabelAsync());
        var requiredError = await wizard.GetFieldValidationMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(requiredError), "Expected a required-field message for an empty work email");

        // Malformed work email — [EmailAddress].
        await wizard.FillEmailFieldAsync("not-an-email");
        await wizard.ClickNextExpectingNoAdvanceAsync();
        Assert.Equal("Email", await wizard.GetActiveStepLabelAsync());
        var formatError = await wizard.GetFieldValidationMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(formatError), "Expected an invalid-format message for a malformed work email");
        Assert.Contains("valid", formatError, StringComparison.OrdinalIgnoreCase);

        // A valid address lets the wizard advance to the Roles step.
        await wizard.FillEmailFieldAsync($"e2e.wizard.{Guid.NewGuid():N}@acme.example");
        await wizard.ClickNextExpectingAdvanceAsync("Roles");
    }
}
