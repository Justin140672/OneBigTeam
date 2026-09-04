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

        Assert.True(await sidebar.HasGroupedMenuItemAsync("HR configuration", "HR Settings"),
            "Expected Laura (HrAdministrator) to see the 'HR Settings' nav link under 'HR configuration'");

        await sidebar.ClickGroupedMenuItemAsync("HR configuration", "HR Settings");
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

        Assert.True(await sidebar.HasGroupedMenuItemAsync("HR configuration", "HR Settings"),
            "Expected Laura (HrAdministrator) to see the 'HR Settings' nav link under 'HR configuration'");
    }

    // ── Employee-number renumbering ────────────────────────────────────────────
    // There is no "Backfill Employee Numbers" button any more. Instead: changing the prefix or
    // minimum length WHILE the company is in Automatic mode pops a "Renumber existing employees?"
    // confirmation, and confirming it queues a background job that rewrites EVERY existing
    // employee's number to the new format. A Manual <-> Automatic mode switch does NOT renumber
    // (existing numbers are left as-is). These exercise that flow through the UI; the handler /
    // job / outbox mechanics are covered by UpdateHrSettingsHandlerTests,
    // EmployeeRenumberSideEffectJobTests and EmployeeRenumberSideEffectEndpointTests.

    private static readonly Guid GraceKimEmployeeId = Guid.Parse("30000000-0000-0000-0000-000000000015");

    [Fact]
    public async Task PrefixChange_ShowsRenumberDialog_AndConfirming_RenumbersExistingEmployees()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToAsync(BetaCorpId);
        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();

        var newPrefix = $"RN{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}-";

        try
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");

            // Changing the prefix in Automatic mode interposes the "Renumber existing employees?"
            // confirmation. A prior renumber from another test in this serial class may still be
            // processing (SET-08 allows only one in flight per company, 409ing the rest) — retry a
            // few times, waiting it out, so this test isn't order-dependent.
            var accepted = false;
            for (var attempt = 1; attempt <= 4 && !accepted; attempt++)
            {
                await hrSettings.GoToAsync(BetaCorpId);
                await hrSettings.SetEmployeeNumberPrefixAsync(newPrefix);
                await hrSettings.ClickSaveAsync();

                Assert.True(await hrSettings.IsRenumberDialogVisibleAsync(),
                    "Expected the 'Renumber existing employees?' confirmation after changing the prefix in Automatic mode");

                await hrSettings.ConfirmRenumberAsync();
                await _page.WaitForSpinnerToClearAsync();

                if (await hrSettings.HasErrorAsync())
                    await _page.WaitForTimeoutAsync(15_000); // another company renumber still running — wait then retry
                else
                    accepted = true;
            }

            Assert.True(accepted, "The renumber-triggering save kept 409ing on a still-processing prior renumber");

            // The renumber runs as a background job — poll an existing Beta employee until her
            // number is rewritten to the new format.
            await empEdit.GoToViewAsync(BetaCorpId, GraceKimEmployeeId);

            var deadline = DateTime.UtcNow.AddSeconds(90);
            var number = await empEdit.GetEmployeeNumberFieldValueAsync();
            while (!number.StartsWith(newPrefix, StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            {
                await _page.WaitForTimeoutAsync(3_000);
                await _page.ReloadAsync();
                number = await empEdit.GetEmployeeNumberFieldValueAsync();
            }

            Assert.StartsWith(newPrefix, number);
        }
        finally
        {
            await hrSettings.GoToAsync(BetaCorpId);
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
        }
    }

    [Fact]
    public async Task PrefixChange_RenumberDialog_Cancel_AbandonsTheSave()
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
            var prefixBefore = await hrSettings.GetEmployeeNumberPrefixAsync();

            await hrSettings.SetEmployeeNumberPrefixAsync($"CX{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}-");
            await hrSettings.ClickSaveAsync();

            Assert.True(await hrSettings.IsRenumberDialogVisibleAsync());
            await hrSettings.CancelRenumberAsync();

            // Cancel abandons the save entirely — reload and the prefix is unchanged.
            await hrSettings.GoToAsync(BetaCorpId);
            Assert.Equal(prefixBefore, await hrSettings.GetEmployeeNumberPrefixAsync());
        }
        finally
        {
            await hrSettings.GoToAsync(BetaCorpId);
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
        }
    }

    [Fact]
    public async Task SwitchingManualToAutomatic_DoesNotShowRenumberDialog()
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
            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync());

            await hrSettings.GoToAsync(BetaCorpId);
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");
            await hrSettings.SetEmployeeNumberPrefixAsync($"MA{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}-");
            await hrSettings.ClickSaveAsync();

            Assert.False(await hrSettings.IsRenumberDialogVisibleAsync(),
                "Manual -> Automatic must not trigger the renumber confirmation — existing numbers are left as-is");

            await _page.WaitForSpinnerToClearAsync();
            Assert.False(await hrSettings.HasErrorAsync());

            await hrSettings.GoToAsync(BetaCorpId);
            Assert.Equal("Automatic", await hrSettings.GetEmployeeNumberModeAsync());
        }
        finally
        {
            await hrSettings.GoToAsync(BetaCorpId);
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
        }
    }
}
