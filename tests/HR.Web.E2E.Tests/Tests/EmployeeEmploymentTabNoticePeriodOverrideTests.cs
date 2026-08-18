using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Override notice period" toggle on the Employee edit page's Employment tab
/// (EmployeeEmploymentTab.razor's Dates card), including the read-only "Notice source"
/// summary that reflects EffectiveNoticePeriodResolver's server-side resolution (Employee
/// override -> Position Profile override -> Company Default).
///
/// Mirrors the existing "Override company default notice period" toggle coverage for
/// Position Profiles in <see cref="PositionProfileNoticePeriodOverrideTests"/> — same
/// SfCheckBox/SfDropDownList/SfNumericTextBox interaction pattern, reused here via the
/// equivalent helpers added to <see cref="EmployeeEditPage"/>.
///
/// Each test creates its own fresh employee (rather than mutating a shared seeded one like
/// Tom Williams or Sarah Chen, which other tests rely on remaining untouched — see
/// CreateEmployeeTests.EmploymentTab_ChangingPositionProfile_PersistsDepartmentAndLocationAfterSave
/// for the same rationale) via "Software Engineer", a seeded Position Profile with
/// Engineering / London Office attached and no notice period override of its own — so any
/// employee assigned to it that doesn't set its own override resolves straight through to
/// the company default (Months / 1, unmodified from CompanySettings' seeded default).
/// </summary>
public sealed class EmployeeEmploymentTabNoticePeriodOverrideTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a fresh Acme employee assigned to the "Software Engineer" position profile
    /// (Engineering / London Office, no notice period override) and returns its id and last
    /// name, leaving the caller positioned on the employee list after creation.
    /// </summary>
    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoticeE2E{unique}";
        var workEmail = $"e2e.notice{unique}@acme.example";

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
        // "Software Engineer" is seeded with both Department (Engineering) and Location
        // (London Office) attached, satisfying those required fields in one step (same
        // reasoning as CreateEmployeeTests' "Senior Software Engineer" selections), and has
        // no notice period override of its own.
        await empEdit.SelectDropdownAsync("Position Profile", "Software Engineer");

        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(_page.Url.TrimEnd('/').Split('/').Last());

        return (employeeId, lastName);
    }

    [Fact]
    public async Task NewEmployee_NoticePeriodOverride_IsUncheckedByDefault_AndSourceIsCompanyDefault()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ClickEmployeeAsync (inside CreateEmployeeAsync) already lands on this employee's
        // edit page (Details tab) — no need to navigate there again.
        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit);
        await empEdit.OpenEmploymentTabAsync();

        Assert.False(await empEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the 'Override notice period' checkbox to be unchecked by default");
        Assert.False(await empEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to stay hidden while the override is unchecked");

        // Neither this employee nor "Software Engineer" (its Position Profile) has an
        // override, so the effective value falls all the way through to the company default.
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

        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit);
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

        var (employeeId, _) = await CreateEmployeeAsync(empList, empEdit);

        // Set an override first, so there's something to turn off.
        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SetOverrideNoticePeriodAsync(true);
        await empEdit.SelectNoticePeriodUnitAsync("Months");
        await empEdit.FillNoticePeriodLengthAsync(2);

        await empEdit.ClickSaveChangesAsync();

        // Reopen and confirm the override was saved before turning it off.
        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.True(await empEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the 'Override notice period' checkbox to be checked before editing it off");
        Assert.Equal("Employee", await empEdit.GetNoticeSourceLabelAsync());

        // Turn the override off and save.
        await empEdit.SetOverrideNoticePeriodAsync(false);
        await empEdit.ClickSaveChangesAsync();

        // Reopen and confirm the override is now unchecked, its fields are hidden, and the
        // effective source falls back through to the Position Profile ("Software Engineer",
        // which has no override of its own) all the way to the Company Default.
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
