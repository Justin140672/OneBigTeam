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
/// ExcludePublicHolidaysFromLeave).
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

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task UpdateSicknessSettings_PersistAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

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
        await login.LoginAsync(LauraEmail);

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
}
