using System.Text.RegularExpressions;
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
/// used only for the read-only dropdown-options test, which depends on his position profile
/// staying "Software Engineer"), Marcus Diallo (HR Advisor, reports to Laura Bennett — used for
/// the manager/location step, cancelled rather than submitted), Priya Sharma (Senior Software
/// Engineer — used for the compensation validation test, cancelled rather than submitted) and
/// David Park (Sales Manager — used for the cancel-mid-wizard test).
///
/// The one test that actually SUBMITS a promotion (PromoteEmployee_WithPositionStepOnly_AppearsInHistoryGrid)
/// used to submit it against Tom Williams, permanently changing his position profile from
/// "Software Engineer" to "Senior Software Engineer" — an irreversible mutation that would have
/// broken PromoteEmployeeDialog_NewPositionProfileDropdown_OnlyOffersVacantProfiles in this same
/// file (which asserts Tom's *current* position is still plain "Software Engineer") under real
/// parallel/re-run execution. It now creates its own fresh employee instead — see that test's own
/// comment for why a fresh "Software Engineer" employee is a safe, valid target.
///
/// That same submitting test also promotes into "QA Engineer" rather than "Senior Software
/// Engineer" — promoting an employee into "Senior Software Engineer" would permanently occupy it,
/// which (like CreateEmployeeTests) would hide it from VacancyDetail's "New Vacancy" Position
/// Profile dropdown that many Recruitment E2E tests depend on, for the rest of the parallel test
/// run. "QA Engineer" is a seeded profile dedicated to this kind of test (same Department/Location
/// as "Senior Software Engineer" — Engineering / London Office — see EmployeesModule's dev seed).
/// </summary>
public sealed class EmployeePromotionTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid SarahChen = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid MarcusDiallo = Guid.Parse("30000000-0000-0000-0000-000000000006");
    private static readonly Guid PriyaSharma = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid DavidPark = Guid.Parse("30000000-0000-0000-0000-000000000008");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a fresh, uniquely-named Acme employee on the "Software Engineer" position profile
    /// (the same profile Tom Williams occupies — multiple employees can share a position profile;
    /// the promotion dialog's "only vacant profiles" dropdown filter only excludes profiles held by
    /// *other* employees when promoting a specific employee, it isn't a creation-time uniqueness
    /// constraint — see EmployeeEmploymentTabNoticePeriodOverrideTests and EmployeeTimelineTabTests,
    /// which already create fresh employees on this same profile) and returns their employee ID.
    /// </summary>
    private async Task<Guid> CreateFreshSoftwareEngineerAsync()
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Promo{unique}";
        var workEmail = $"e2e.promo{unique}@acme.example";

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
        await empEdit.SelectDropdownAsync("Position Profile", "Software Engineer");
        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
    }

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

        var freshEmployeeId = await CreateFreshSoftwareEngineerAsync();

        await empEdit.GoToAsync(AcmeId, freshEmployeeId);
        await empEdit.OpenPromotionHistoryTabAsync();

        await wizard.OpenAsync();

        Assert.Equal("1. Position", await wizard.GetActiveStepLabelAsync());
        Assert.Contains("Software Engineer", await wizard.GetCurrentPositionTextAsync());

        await wizard.SelectNewPositionProfileAsync("QA Engineer");
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

        Assert.Equal("QA Engineer", await wizard.GetConfirmationValueAsync("New Position"));
        Assert.Equal("Annual review promotion", await wizard.GetConfirmationValueAsync("Reason"));

        await wizard.SubmitAsync();

        Assert.False(await wizard.IsVisibleAsync(), "Expected the wizard dialog to close after a successful promotion");

        var row = empEdit.PromotionHistoryRow("QA Engineer");
        Assert.True(await row.First.IsVisibleAsync(), "Expected the newly created promotion to appear in the history grid");

        var rowText = await row.First.TextContentAsync();
        Assert.Contains("Software Engineer", rowText);
        Assert.Contains("QA Engineer", rowText);
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
        // PromoteEmployeeDialog.ResetForm), but Salary itself is NOT left blank here: OnOpenedAsync
        // pre-fills Model.CompensationSalary from the employee's current compensation (Priya
        // Sharma has an existing salary) as a UX convenience so reviewers don't have to re-type
        // figures that usually don't change. Explicitly clear it to actually exercise
        // ValidateCompensation's "Please enter a salary greater than 0." rule when attempting to
        // advance to the Confirm step.
        await wizard.FillCompensationSalaryAsync("");
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
