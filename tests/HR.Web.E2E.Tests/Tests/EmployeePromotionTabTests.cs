using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Promotion History tab and the "Promote Employee" wizard dialog on the employee
/// edit page.
///
/// Uses several seeded Acme employees so each test can freely add promotion records without
/// affecting the others (see EmployeesModule's dev seed): Sarah Chen (CTO, untouched — used only
/// for the empty-state assertion), Tom Williams (Software Engineer, reports to James Okafor —
/// used for the plain single-step promotion), Marcus Diallo (HR Advisor, reports to Laura
/// Bennett — used for the manager/location step), Priya Sharma (Senior Software Engineer — used
/// for the compensation validation test) and David Park (Sales Manager — used for the
/// cancel-mid-wizard test).
/// </summary>
[Collection("E2E")]
public sealed class EmployeePromotionTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid SarahChen = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid MarcusDiallo = Guid.Parse("30000000-0000-0000-0000-000000000006");
    private static readonly Guid PriyaSharma = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid DavidPark = Guid.Parse("30000000-0000-0000-0000-000000000008");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task PromotionHistoryTab_ShowsEmptyState_ForEmployeeWithNoPromotions()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenPromotionHistoryTabAsync();

        Assert.True(await empEdit.HasNoPromotionsMessageAsync(),
            "Expected the 'No promotions recorded for this employee.' empty state for an employee with no promotion history");
        Assert.False(await empEdit.HasPromotionHistoryGridAsync(),
            "Did not expect a promotion history grid to render alongside the empty state");
    }

    /// <summary>
    /// The "New Position Profile" dropdown only offers position profiles with no *other* employee
    /// currently assigned to them (see PromoteEmployeeDialog.razor's OnOpenedAsync, which excludes
    /// the promotee themselves — "e.Id != EmployeeId" — from the occupied set) — "CTO" (Sarah
    /// Chen's) is occupied by someone else, so it should not appear as an option when promoting
    /// Tom, but "Software Engineer" (Tom Williams' own current position) is deliberately still
    /// offered since Tom occupying it doesn't make it unavailable to Tom himself.
    /// </summary>
    [Fact]
    public async Task PromoteEmployeeDialog_NewPositionProfileDropdown_OnlyOffersVacantProfiles()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenPromotionHistoryTabAsync();
        await wizard.OpenAsync();

        var options = await wizard.GetNewPositionProfileDropdownOptionsAsync();

        Assert.Contains(options, o => o.Contains("Software Engineer") && !o.Contains("Senior"));
        Assert.DoesNotContain(options, o => o.Contains("CTO"));
    }

    [Fact]
    public async Task PromoteEmployee_WithPositionStepOnly_AppearsInHistoryGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenPromotionHistoryTabAsync();

        await wizard.OpenAsync();

        Assert.Equal("1. Position", await wizard.GetActiveStepLabelAsync());
        Assert.Contains("Software Engineer", await wizard.GetCurrentPositionTextAsync());

        await wizard.SelectNewPositionProfileAsync("Senior Software Engineer");
        await wizard.FillEffectiveDateAsync("01/08/2026");
        await wizard.FillReasonAsync("Annual review promotion");
        await wizard.FillNotesAsync("Consistently exceeded expectations this cycle.");

        // Skip step 2 (Manager & Location) and step 3 (Compensation) entirely.
        await wizard.ClickNextAsync();
        Assert.Equal("2. Manager & Location", await wizard.GetActiveStepLabelAsync());

        await wizard.ClickNextAsync();
        Assert.Equal("3. Compensation", await wizard.GetActiveStepLabelAsync());

        await wizard.ClickNextAsync();
        Assert.Equal("4. Confirm", await wizard.GetActiveStepLabelAsync());

        Assert.Equal("Senior Software Engineer", await wizard.GetConfirmationValueAsync("New Position"));
        Assert.Equal("Annual review promotion", await wizard.GetConfirmationValueAsync("Reason"));

        await wizard.SubmitAsync();

        Assert.False(await wizard.IsVisibleAsync(), "Expected the wizard dialog to close after a successful promotion");

        var row = empEdit.PromotionHistoryRow("Senior Software Engineer");
        Assert.True(await row.First.IsVisibleAsync(), "Expected the newly created promotion to appear in the history grid");

        var rowText = await row.First.TextContentAsync();
        Assert.Contains("Software Engineer", rowText);
        Assert.Contains("Senior Software Engineer", rowText);
        Assert.Contains("Annual review promotion", rowText);
        Assert.Contains("Laura Bennett", rowText);
    }

    [Fact]
    public async Task PromoteEmployee_WithManagerAndLocationChange_ReflectedInConfirmationSummary()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusDiallo);
        await empEdit.OpenPromotionHistoryTabAsync();
        await wizard.OpenAsync();

        await wizard.SelectNewPositionProfileAsync("HR Manager");
        await wizard.FillEffectiveDateAsync("01/09/2026");
        await wizard.FillReasonAsync("Stepping up to HR Manager");
        await wizard.ClickNextAsync();

        Assert.Equal("2. Manager & Location", await wizard.GetActiveStepLabelAsync());

        await wizard.CheckChangeManagerAsync();
        await wizard.SelectNewManagerAsync("Sarah Chen");

        await wizard.CheckChangeLocationAsync();
        await wizard.SelectNewLocationAsync("London Office");

        await wizard.ClickNextAsync();
        Assert.Equal("3. Compensation", await wizard.GetActiveStepLabelAsync());

        await wizard.ClickNextAsync();
        Assert.Equal("4. Confirm", await wizard.GetActiveStepLabelAsync());

        Assert.Equal("HR Manager", await wizard.GetConfirmationValueAsync("New Position"));
        Assert.Contains("Sarah Chen", await wizard.GetConfirmationValueAsync("New Manager"));
        Assert.Contains("London Office", await wizard.GetConfirmationValueAsync("New Location"));

        // The optional-step reveal/confirmation is the behaviour under test here — cancel rather
        // than submit, to avoid this promotion leaking into any other test that inspects Marcus
        // Diallo's promotion history or reporting line.
        await wizard.CancelAsync();
        await wizard.ConfirmDiscardChangesAsync();

        Assert.False(await wizard.IsVisibleAsync(), "Expected the wizard dialog to close after cancelling");
    }

    [Fact]
    public async Task PromoteEmployee_CompensationStepChecked_RequiresSalaryFields()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, PriyaSharma);
        await empEdit.OpenPromotionHistoryTabAsync();
        await wizard.OpenAsync();

        await wizard.SelectNewPositionProfileAsync("Chief Technology Officer");
        await wizard.FillEffectiveDateAsync("01/10/2026");
        await wizard.FillReasonAsync("Succession planning");
        await wizard.ClickNextAsync();
        Assert.Equal("2. Manager & Location", await wizard.GetActiveStepLabelAsync());

        await wizard.ClickNextAsync();
        Assert.Equal("3. Compensation", await wizard.GetActiveStepLabelAsync());

        await wizard.CheckCreateCompensationChangeAsync();

        // Salary type defaults to "Annual" and currency defaults to "GBP" (see
        // PromoteEmployeeDialog.ResetForm), so leaving Salary itself empty is enough to trigger
        // ValidateCompensation's "Please enter a salary greater than 0." rule when attempting to
        // advance to the Confirm step.
        await wizard.ClickNextAsync();

        Assert.Equal("3. Compensation", await wizard.GetActiveStepLabelAsync());
        var error = await wizard.GetGlobalErrorAsync();
        Assert.NotNull(error);
        Assert.Contains("salary", error, StringComparison.OrdinalIgnoreCase);

        // Fill in a valid salary and confirm the wizard now advances past the step.
        await wizard.FillCompensationSalaryAsync("150000");
        await wizard.ClickNextAsync();
        Assert.Equal("4. Confirm", await wizard.GetActiveStepLabelAsync());

        await wizard.CancelAsync();
    }

    [Fact]
    public async Task CancellingWizard_MidWay_DoesNotCreatePromotion()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var wizard = new PromoteEmployeeDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, DavidPark);
        await empEdit.OpenPromotionHistoryTabAsync();

        Assert.True(await empEdit.HasNoPromotionsMessageAsync(),
            "Expected David Park to start with no promotion history for this test to be meaningful");

        await wizard.OpenAsync();

        await wizard.SelectNewPositionProfileAsync("Sales Manager");
        await wizard.FillEffectiveDateAsync("01/11/2026");
        await wizard.FillReasonAsync("Should never be submitted");
        await wizard.ClickNextAsync();
        Assert.Equal("2. Manager & Location", await wizard.GetActiveStepLabelAsync());

        await wizard.CancelAsync();
        await wizard.ConfirmDiscardChangesAsync();

        Assert.False(await wizard.IsVisibleAsync(), "Expected the wizard dialog to close after cancelling");

        // Grid/tab state should be exactly as before — no promotion was created.
        Assert.True(await empEdit.HasNoPromotionsMessageAsync(),
            "Expected the empty state to still be shown after cancelling the wizard mid-way");
        Assert.False(await empEdit.HasPromotionHistoryGridAsync(),
            "Did not expect a promotion history grid after cancelling the wizard mid-way");
    }
}
