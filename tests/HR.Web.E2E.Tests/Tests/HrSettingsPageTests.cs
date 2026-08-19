using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the standalone HR Settings page (/companies/{id}/hr-settings), which now holds all
/// the HR-policy fields previously on the Company Settings tab (Working Week, HoursPerDay,
/// LeaveYearStartMonth, DefaultHolidayAllowance, ProbationMonths, ExcludePublicHolidaysFrom*,
/// DisplaySalaryOnEmployeeProfile, Sickness, Document Acknowledgement, Leaving Process, and
/// Employee Numbering).
///
/// Access is gated on Session.IsHrAdministrator. The read-only/permission-boundary tests below
/// use Laura Bennett (laura.bennett@acme.example, HrAdministrator) and Priya Shah
/// (priya.shah@acme.example, CompanyAdministrator-only, to confirm the permission gap fix: she
/// can no longer reach this page or see its nav link) — the same personas used throughout the
/// suite for HR-administrator-only pages.
///
/// The tests that actually mutate Employee Numbering mode (which flips the shared
/// company_settings row to Automatic mid-test, hiding the Employee Number field on the New
/// Employee form for anyone else on that tenant — see FillEmployeeNumberAsync's own remarks) use
/// Grace Kim on Beta Corp (grace.kim@betacorp.example, HrAdministrator) instead of Laura on
/// Acme, even though this class already serializes against itself (HrSettingsSerial): that only
/// prevents these tests from racing each other, not from racing the ~139 other, ordinary
/// role-fixed classes that create Acme employees via the same New Employee form and expect
/// Manual mode. Using a dedicated tenant removes the race at its source instead of requiring
/// every employee-creation test to join a shared serialization group.
/// </summary>
public sealed class HrSettingsPageTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string CompanyAdminEmail = "priya.shah@acme.example";
    private const string BetaHrAdminEmail = "grace.kim@betacorp.example";

    [Fact]
    public async Task LoadPage_RendersAllExpectedSections()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await hrSettings.GoToAsync(AcmeId);

        Assert.True(await hrSettings.IsWorkingWeekSectionVisibleAsync(), "Expected the 'Working Week' section to render");
        Assert.True(await hrSettings.IsSicknessSectionVisibleAsync(), "Expected the 'Sickness' section to render");
        Assert.True(await hrSettings.IsDocumentAcknowledgementSectionVisibleAsync(), "Expected the 'Document Acknowledgement' section to render");
        Assert.True(await hrSettings.IsLeavingProcessSectionVisibleAsync(), "Expected the 'Leaving Process' section to render");
        Assert.True(await hrSettings.IsEmployeeNumberingSectionVisibleAsync(), "Expected the 'Employee Numbering' section to render");
    }

    [Fact]
    public async Task HrAdministrator_CanNavigateToHrSettings_ViaSidebarLink()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("HR Settings"),
            "Expected Laura (HrAdministrator) to see the 'HR Settings' nav link");

        await sidebar.ClickTopLevelMenuItemAsync("HR Settings");
        await _page.WaitForURLAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr-settings", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task UpdateRepresentativeFieldsAcrossAllSections_PersistAfterReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToAsync(BetaCorpId);

        // Capture initial state so we can restore it afterwards and avoid leaking state into
        // other tests/fixtures that rely on the seeded defaults for this company.
        var initialSaturday = await hrSettings.IsWorkingDayCheckedAsync("Saturday");
        var initialHours = await hrSettings.GetHoursPerDayAsync();
        var initialAllowance = await hrSettings.GetDefaultHolidayAllowanceAsync();
        var initialProbation = await hrSettings.GetProbationMonthsAsync();
        var initialNoticePreset = await hrSettings.GetNoticePeriodPresetAsync();
        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();
        var initialPrefix = await hrSettings.IsEmployeeNumberAutomaticFieldsVisibleAsync()
            ? await hrSettings.GetEmployeeNumberPrefixAsync()
            : null;
        var initialNextNumber = await hrSettings.IsEmployeeNumberAutomaticFieldsVisibleAsync()
            ? await hrSettings.GetNextEmployeeNumberAsync()
            : (int?)null;
        var initialMinLength = await hrSettings.IsEmployeeNumberAutomaticFieldsVisibleAsync()
            ? await hrSettings.GetEmployeeNumberMinimumLengthAsync()
            : (int?)null;

        var desiredSaturday = !initialSaturday;

        try
        {
            await hrSettings.SetWorkingDayAsync("Saturday", desiredSaturday);
            await hrSettings.SetHoursPerDayAsync(7.5m);
            await hrSettings.SetDefaultHolidayAllowanceAsync(28);
            await hrSettings.SetProbationMonthsAsync(6);
            await hrSettings.SelectNoticePeriodPresetAsync("3 months");
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");
            await hrSettings.SetEmployeeNumberPrefixAsync("STF-");
            await hrSettings.SetNextEmployeeNumberAsync(15);
            await hrSettings.SetEmployeeNumberMinimumLengthAsync(6);

            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync(),
                "Expected no error after saving representative HR settings fields");

            // Reload the page for real (re-navigate) to exercise the settings-hydration path
            // server-side, not just in-memory Blazor state.
            await hrSettings.GoToAsync(BetaCorpId);

            Assert.Equal(desiredSaturday, await hrSettings.IsWorkingDayCheckedAsync("Saturday"));
            Assert.Equal(7.5m, await hrSettings.GetHoursPerDayAsync());
            Assert.Equal(28m, await hrSettings.GetDefaultHolidayAllowanceAsync());
            Assert.Equal(6, await hrSettings.GetProbationMonthsAsync());
            Assert.Equal("3 months", await hrSettings.GetNoticePeriodPresetAsync());
            Assert.Equal("Automatic", await hrSettings.GetEmployeeNumberModeAsync());
            Assert.Equal("STF-", await hrSettings.GetEmployeeNumberPrefixAsync());
            Assert.Equal(15, await hrSettings.GetNextEmployeeNumberAsync());
            Assert.Equal(6, await hrSettings.GetEmployeeNumberMinimumLengthAsync());
        }
        finally
        {
            // Restore original values so this test doesn't leak state into other tests/fixtures
            // that rely on the seeded defaults for this company.
            await hrSettings.SetWorkingDayAsync("Saturday", initialSaturday);
            await hrSettings.SetHoursPerDayAsync(initialHours);
            await hrSettings.SetDefaultHolidayAllowanceAsync(initialAllowance);
            await hrSettings.SetProbationMonthsAsync(initialProbation);
            await hrSettings.SelectNoticePeriodPresetAsync(initialNoticePreset);
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            if (initialMode == "Automatic")
            {
                await hrSettings.SetEmployeeNumberPrefixAsync(initialPrefix ?? "");
                await hrSettings.SetNextEmployeeNumberAsync(initialNextNumber ?? 1);
                await hrSettings.SetEmployeeNumberMinimumLengthAsync(initialMinLength ?? 1);
            }
            await hrSettings.SaveAsync();
        }
    }

    [Fact]
    public async Task EmployeeNumberMode_TogglesVisibility_Of_AutomaticOnlyFields()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToAsync(BetaCorpId);

        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();

        try
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Manual");
            Assert.False(await hrSettings.IsEmployeeNumberAutomaticFieldsVisibleAsync());

            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");
            Assert.True(await hrSettings.IsEmployeeNumberAutomaticFieldsVisibleAsync());
        }
        finally
        {
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
        }
    }

    [Fact]
    public async Task EmployeeNumberPreview_UpdatesAsPrefixNextNumberAndMinimumLengthChange()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToAsync(BetaCorpId);

        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();

        try
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");

            await hrSettings.SetEmployeeNumberPrefixAsync("EMP-");
            await hrSettings.SetNextEmployeeNumberAsync(7);
            await hrSettings.SetEmployeeNumberMinimumLengthAsync(4);

            Assert.Equal("Preview: EMP-0007", await hrSettings.GetEmployeeNumberPreviewAsync());

            await hrSettings.SetNextEmployeeNumberAsync(42);
            Assert.Equal("Preview: EMP-0042", await hrSettings.GetEmployeeNumberPreviewAsync());
        }
        finally
        {
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
        }
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_HrSettingsPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr-settings");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/hr-settings", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no IsHrAdministrator) to be redirected away " +
            $"from the HR Settings page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task CompanyAdministrator_DoesNotSee_HrSettingsNavLink()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        Assert.False(await sidebar.HasTopLevelMenuItemAsync("HR Settings"),
            "Did not expect Priya (CompanyAdministrator-only) to see the 'HR Settings' nav link");
    }

    [Fact]
    public async Task HrAdministrator_Sees_HrSettingsNavLink()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("HR Settings"),
            "Expected Laura (HrAdministrator) to see the 'HR Settings' nav link");
    }
}
