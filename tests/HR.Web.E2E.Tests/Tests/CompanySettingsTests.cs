using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Company Settings tab, including the sickness-related fields:
/// - ExcludePublicHolidaysFromSickness (checkbox)
/// - FitNoteRequiredAfterDays (nullable numeric)
/// - ReturnToWorkRequiredAfterDays (nullable numeric)
///
/// as well as the pre-existing settings (working week, HoursPerDay,
/// LeaveYearStartMonth, DefaultHolidayAllowance, ProbationMonths,
/// ExcludePublicHolidaysFromLeave), and the "Leaving Process" section
/// (slice 1 of the Employee Leaving Process feature):
/// - Notice period preset dropdown (fixed presets, or "Custom duration"
///   revealing a Unit dropdown + Length numeric)
/// - AutoDisableAccessOnLeavingDate (checkbox)
///
/// TimeZone and Locale are saved via UpdateCompanySettingsRequest on the
/// backend but are no longer editable from this tab's UI, so they're not
/// exercised here.
///
/// All of these are saved via UpdateCompanySettingsRequest and re-hydrated on
/// CompanyEdit's OnInitializedAsync from GetCompanySettingsAsync, so the tests
/// reload the page after saving to confirm the values round-trip through the
/// API rather than just checking in-memory Blazor state.
/// </summary>
[Collection("E2E")]
public sealed class CompanySettingsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // CompanyEdit's edit mode (LoadAsync) gates on Session.CanManageCompany, which the
    // company:manage policy restricts to CompanyAdministrator — HrAdministrator no longer
    // qualifies, so these tests need a CompanyAdministrator-only persona.
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task UpdateSicknessSettings_PersistAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // Navigate to Acme's edit page and open the Settings tab.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Capture the initial checkbox state so we can toggle it to the opposite value.
        var initialExcludeHolidays = await companyEdit.IsExcludePublicHolidaysFromSicknessCheckedAsync();
        var desiredExcludeHolidays = !initialExcludeHolidays;

        await companyEdit.SetExcludePublicHolidaysFromSicknessAsync(desiredExcludeHolidays);
        await companyEdit.SetFitNoteRequiredAfterDaysAsync(14);
        await companyEdit.SetReturnToWorkRequiredAfterDaysAsync(7);

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving the sickness settings");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.Equal(desiredExcludeHolidays, await companyEdit.IsExcludePublicHolidaysFromSicknessCheckedAsync());
        Assert.Equal(14, await companyEdit.GetFitNoteRequiredAfterDaysAsync());
        Assert.Equal(7, await companyEdit.GetReturnToWorkRequiredAfterDaysAsync());
    }

    [Fact]
    public async Task UpdateAllCompanySettings_PersistAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Capture initial toggle-able states so we can flip them to a known opposite value.
        var initialExcludeSickness = await companyEdit.IsExcludePublicHolidaysFromSicknessCheckedAsync();
        var initialExcludeLeave    = await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync();
        var initialSaturday        = await companyEdit.IsWorkingDayCheckedAsync("Saturday");

        var desiredExcludeSickness = !initialExcludeSickness;
        var desiredExcludeLeave    = !initialExcludeLeave;
        var desiredSaturday        = !initialSaturday;

        // Working week.
        await companyEdit.SetWorkingDayAsync("Saturday", desiredSaturday);

        // Numeric fields.
        await companyEdit.SetHoursPerDayAsync(7.5m);
        await companyEdit.SetDefaultHolidayAllowanceAsync(28);
        await companyEdit.SetProbationMonthsAsync(6);
        await companyEdit.SetFitNoteRequiredAfterDaysAsync(21);
        await companyEdit.SetReturnToWorkRequiredAfterDaysAsync(10);

        // Dropdown.
        await companyEdit.SelectLeaveYearStartMonthAsync("April");

        // Checkboxes.
        await companyEdit.SetExcludePublicHolidaysFromLeaveAsync(desiredExcludeLeave);
        await companyEdit.SetExcludePublicHolidaysFromSicknessAsync(desiredExcludeSickness);

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving the full set of company settings");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync end-to-end, for every field on the tab.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.Equal(desiredSaturday, await companyEdit.IsWorkingDayCheckedAsync("Saturday"));
        Assert.Equal(7.5m, await companyEdit.GetHoursPerDayAsync());
        Assert.Equal(28m, await companyEdit.GetDefaultHolidayAllowanceAsync());
        Assert.Equal(6, await companyEdit.GetProbationMonthsAsync());
        Assert.Equal(21, await companyEdit.GetFitNoteRequiredAfterDaysAsync());
        Assert.Equal(10, await companyEdit.GetReturnToWorkRequiredAfterDaysAsync());
        Assert.Equal("April", await companyEdit.GetLeaveYearStartMonthAsync());
        Assert.Equal(desiredExcludeLeave, await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync());
        Assert.Equal(desiredExcludeSickness, await companyEdit.IsExcludePublicHolidaysFromSicknessCheckedAsync());
    }

    [Fact]
    public async Task UpdateDisplaySalaryOnEmployeeProfile_PersistsAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Capture the initial checkbox state so we can toggle it to the opposite value.
        var initialDisplaySalary = await companyEdit.IsDisplaySalaryOnEmployeeProfileCheckedAsync();
        var desiredDisplaySalary = !initialDisplaySalary;

        await companyEdit.SetDisplaySalaryOnEmployeeProfileAsync(desiredDisplaySalary);

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving the 'display salary on employee profile' setting");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.Equal(desiredDisplaySalary, await companyEdit.IsDisplaySalaryOnEmployeeProfileCheckedAsync());

        // Restore the original value so this test doesn't leak state into other
        // tests/fixtures that rely on the seeded default for this company.
        await companyEdit.SetDisplaySalaryOnEmployeeProfileAsync(initialDisplaySalary);
        await companyEdit.SaveAsync();
    }

    [Fact]
    public async Task LeavingProcessSection_IsVisibleOnSettingsTab()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // The preset dropdown always shows some selected value (either a fixed preset or
        // "Custom duration"), so simply reading it confirms the control rendered.
        var preset = await companyEdit.GetNoticePeriodPresetAsync();
        Assert.False(string.IsNullOrWhiteSpace(preset));

        // The checkbox is always present and reflects a concrete boolean value either way,
        // so just calling the getter without it throwing confirms the control rendered.
        await companyEdit.IsAutoDisableAccessOnLeavingDateCheckedAsync();
    }

    [Fact]
    public async Task UpdateNoticePeriodPreset_PersistsAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Capture the initial preset so the test can restore it afterwards — this is a new,
        // still-evolving feature (slice 1 of 8) and later slices may rely on Acme's seeded
        // notice period, so avoid leaking a changed value into other tests/fixtures.
        var initialPreset = await companyEdit.GetNoticePeriodPresetAsync();

        await companyEdit.SelectNoticePeriodPresetAsync("3 months");

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving the notice period preset");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.Equal("3 months", await companyEdit.GetNoticePeriodPresetAsync());

        // Restore the original preset so this test doesn't leak state into other
        // tests/fixtures that rely on the seeded default for this company.
        await companyEdit.SelectNoticePeriodPresetAsync(initialPreset);
        await companyEdit.SaveAsync();
    }

    [Fact]
    public async Task UpdateNoticePeriodCustomDuration_PersistsAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Capture the initial preset so the test can restore it afterwards (see comment in
        // UpdateNoticePeriodPreset_PersistsAfterReload).
        var initialPreset = await companyEdit.GetNoticePeriodPresetAsync();

        await companyEdit.SelectNoticePeriodPresetAsync("Custom duration");
        await companyEdit.WaitForNoticePeriodCustomControlsAsync();

        await companyEdit.SelectNoticePeriodUnitAsync("Weeks");
        await companyEdit.SetNoticePeriodLengthAsync(5);

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving a custom-duration notice period");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();
        await companyEdit.WaitForNoticePeriodCustomControlsAsync();

        Assert.Equal("Custom duration", await companyEdit.GetNoticePeriodPresetAsync());
        Assert.Equal("Weeks", await companyEdit.GetNoticePeriodUnitAsync());
        Assert.Equal(5, await companyEdit.GetNoticePeriodLengthAsync());

        // Restore the original preset so this test doesn't leak state into other
        // tests/fixtures that rely on the seeded default for this company.
        await companyEdit.SelectNoticePeriodPresetAsync(initialPreset);
        await companyEdit.SaveAsync();
    }

    [Fact]
    public async Task UpdateAutoDisableAccessOnLeavingDate_PersistsAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Capture the initial checkbox state so we can toggle it to the opposite value.
        var initialAutoDisableAccess = await companyEdit.IsAutoDisableAccessOnLeavingDateCheckedAsync();
        var desiredAutoDisableAccess = !initialAutoDisableAccess;

        await companyEdit.SetAutoDisableAccessOnLeavingDateAsync(desiredAutoDisableAccess);

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving the 'auto-disable access on leaving date' setting");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.Equal(desiredAutoDisableAccess, await companyEdit.IsAutoDisableAccessOnLeavingDateCheckedAsync());

        // Restore the original value so this test doesn't leak state into other
        // tests/fixtures that rely on the seeded default for this company.
        await companyEdit.SetAutoDisableAccessOnLeavingDateAsync(initialAutoDisableAccess);
        await companyEdit.SaveAsync();
    }

    // ── Employee Numbering ──────────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeNumberMode_TogglesVisibility_Of_AutomaticOnlyFields()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialMode = await companyEdit.GetEmployeeNumberModeAsync();

        await companyEdit.SelectEmployeeNumberModeAsync("Manual");
        Assert.False(await companyEdit.IsEmployeeNumberAutomaticFieldsVisibleAsync());

        await companyEdit.SelectEmployeeNumberModeAsync("Automatic");
        Assert.True(await companyEdit.IsEmployeeNumberAutomaticFieldsVisibleAsync());

        // Restore the original mode so this test doesn't leak state into other tests/fixtures.
        await companyEdit.SelectEmployeeNumberModeAsync(initialMode);
        await companyEdit.SaveAsync();
    }

    [Fact]
    public async Task EmployeeNumberPreview_UpdatesAsPrefixNextNumberAndMinimumLengthChange()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialMode = await companyEdit.GetEmployeeNumberModeAsync();

        await companyEdit.SelectEmployeeNumberModeAsync("Automatic");

        await companyEdit.SetEmployeeNumberPrefixAsync("EMP-");
        await companyEdit.SetNextEmployeeNumberAsync(7);
        await companyEdit.SetEmployeeNumberMinimumLengthAsync(4);

        Assert.Equal("Preview: EMP-0007", await companyEdit.GetEmployeeNumberPreviewAsync());

        await companyEdit.SetNextEmployeeNumberAsync(42);
        Assert.Equal("Preview: EMP-0042", await companyEdit.GetEmployeeNumberPreviewAsync());

        // Restore the original mode so this test doesn't leak state into other tests/fixtures.
        await companyEdit.SelectEmployeeNumberModeAsync(initialMode);
        await companyEdit.SaveAsync();
    }

    [Fact]
    public async Task EmployeeNumberSettings_PersistAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialMode = await companyEdit.GetEmployeeNumberModeAsync();
        var initialPrefix = await companyEdit.IsEmployeeNumberAutomaticFieldsVisibleAsync()
            ? await companyEdit.GetEmployeeNumberPrefixAsync()
            : null;
        var initialNextNumber = await companyEdit.IsEmployeeNumberAutomaticFieldsVisibleAsync()
            ? await companyEdit.GetNextEmployeeNumberAsync()
            : (int?)null;
        var initialMinLength = await companyEdit.IsEmployeeNumberAutomaticFieldsVisibleAsync()
            ? await companyEdit.GetEmployeeNumberMinimumLengthAsync()
            : (int?)null;

        await companyEdit.SelectEmployeeNumberModeAsync("Automatic");
        await companyEdit.SetEmployeeNumberPrefixAsync("STF-");
        await companyEdit.SetNextEmployeeNumberAsync(15);
        await companyEdit.SetEmployeeNumberMinimumLengthAsync(6);

        await companyEdit.SaveAsync();
        Assert.False(await companyEdit.HasErrorAsync(),
            "Expected no error after saving the employee numbering settings");

        // Reload the page for real (re-navigate) to exercise the settings-hydration
        // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.Equal("Automatic", await companyEdit.GetEmployeeNumberModeAsync());
        Assert.Equal("STF-", await companyEdit.GetEmployeeNumberPrefixAsync());
        Assert.Equal(15, await companyEdit.GetNextEmployeeNumberAsync());
        Assert.Equal(6, await companyEdit.GetEmployeeNumberMinimumLengthAsync());

        // Restore the original values so this test doesn't leak state into other
        // tests/fixtures that rely on the seeded default for this company.
        await companyEdit.SelectEmployeeNumberModeAsync(initialMode);
        if (initialMode == "Automatic")
        {
            await companyEdit.SetEmployeeNumberPrefixAsync(initialPrefix ?? "");
            await companyEdit.SetNextEmployeeNumberAsync(initialNextNumber ?? 1);
            await companyEdit.SetEmployeeNumberMinimumLengthAsync(initialMinLength ?? 1);
        }
        await companyEdit.SaveAsync();
    }
}
