using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Company Settings tab (CompanySettingsTab.razor), which has been slimmed down to
/// only the "Regional" section (TimeZone, Locale — plain HrTextBox inputs) and the "Backfill
/// Employee Timeline" subsection. All other HR-policy fields that used to live here (Working
/// Week, HoursPerDay, LeaveYearStartMonth, DefaultHolidayAllowance, ProbationMonths,
/// ExcludePublicHolidaysFrom*, DisplaySalaryOnEmployeeProfile, Sickness, Document
/// Acknowledgement, Leaving Process, Employee Numbering) have moved to the standalone HR
/// Settings page (/companies/{id}/hr-settings) — see HrSettingsPageTests.
///
/// TimeZone and Locale are saved via UpdateCompanySettingsRequest and re-hydrated on
/// CompanyEdit's OnInitializedAsync from GetCompanySettingsAsync, so the round-trip test reloads
/// the page after saving to confirm the values persist server-side rather than just checking
/// in-memory Blazor state.
/// </summary>
public sealed class CompanySettingsTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // CompanyEdit's edit mode (LoadAsync) gates on Session.CanManageCompany, which the
    // company:manage policy restricts to CompanyAdministrator — HrAdministrator no longer
    // qualifies, so these tests need a CompanyAdministrator-only persona.
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task UpdateTimeZoneAndLocale_PersistAfterReload()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialTimeZone = await companyEdit.GetTimeZoneAsync();
        var initialLocale   = await companyEdit.GetLocaleAsync();

        try
        {
            await companyEdit.SetTimeZoneAsync("Europe/London");
            await companyEdit.SetLocaleAsync("en-GB");

            await companyEdit.SaveAsync();
            Assert.False(await companyEdit.HasErrorAsync(),
                "Expected no error after saving TimeZone/Locale");

            // Reload the page for real (re-navigate) to exercise the settings-hydration
            // path in CompanyEdit.OnInitializedAsync, not just in-memory Blazor state.
            await companyEdit.GoToAsync(AcmeId);
            await companyEdit.OpenSettingsTabAsync();

            Assert.Equal("Europe/London", await companyEdit.GetTimeZoneAsync());
            Assert.Equal("en-GB", await companyEdit.GetLocaleAsync());
        }
        finally
        {
            // Restore original values so this test doesn't leak state into other
            // tests/fixtures that rely on the seeded default for this company.
            await companyEdit.SetTimeZoneAsync(initialTimeZone);
            await companyEdit.SetLocaleAsync(initialLocale);
            await companyEdit.SaveAsync();
        }
    }

    /// <summary>
    /// Priya Shah (CompanyAdministrator only) can reach the Settings tab but does not hold
    /// Session.CanManageEmployees, so the "Backfill Employee Timeline…" button must not render
    /// for her — see BackfillEmployeeTimelineTests for the fuller documentation of this
    /// persona/policy gap (no seeded persona holds both CanManageCompany and CanManageEmployees).
    /// </summary>
    [Fact]
    public async Task BackfillEmployeeTimelineButton_IsNotVisible_ForCompanyAdministratorWithoutEmployeeManagePermission()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.False(await companyEdit.IsBackfillEmployeeTimelineButtonVisibleAsync(),
            "Did not expect the 'Backfill Employee Timeline…' button to be visible for a " +
            "CompanyAdministrator-only persona (Session.CanManageEmployees should be false)");
    }
}
