using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

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
            await hrSettings.SetEmployeeNumberMinimumLengthAsync(6);

            await SaveAndWaitForRenumberToSettleAsync(hrSettings, "STF-");
            Assert.False(await hrSettings.HasErrorAsync(),
                $"Expected no error after saving representative HR settings fields, got: {await hrSettings.GetErrorTextAsync()}");

            await hrSettings.GoToAsync(BetaCorpId);

            Assert.Equal(desiredSaturday, await hrSettings.IsWorkingDayCheckedAsync("Saturday"));
            Assert.Equal(7.5m, await hrSettings.GetHoursPerDayAsync());
            Assert.Equal(28m, await hrSettings.GetDefaultHolidayAllowanceAsync());
            Assert.Equal(6, await hrSettings.GetProbationMonthsAsync());
            Assert.Equal("3 months", await hrSettings.GetNoticePeriodPresetAsync());
            Assert.Equal("Automatic", await hrSettings.GetEmployeeNumberModeAsync());
            Assert.Equal("STF-", await hrSettings.GetEmployeeNumberPrefixAsync());
            Assert.Equal(6, await hrSettings.GetEmployeeNumberMinimumLengthAsync());

            // NextEmployeeNumber is deliberately NOT asserted above: changing the prefix/minimum
            // length while in Automatic mode triggers a background renumber
            // (EmployeeRenumberSideEffectJob, see UpdateHrSettings/Handler.cs) that recalculates
            // NextEmployeeNumber from the company's actual employee count, overwriting whatever
            // value was submitted in the same save — asserting a hand-picked number here would be
            // asserting the wrong (and non-deterministic, race-dependent-on-the-background-job)
            // behaviour. Verify NextEmployeeNumber's own persistence separately, via a save that
            // does NOT also change the prefix/minimum length, so no renumber is triggered.
            await hrSettings.SetNextEmployeeNumberAsync(15);
            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync(),
                $"Expected no error after saving the next employee number on its own, got: {await hrSettings.GetErrorTextAsync()}");

            await hrSettings.GoToAsync(BetaCorpId);
            Assert.Equal(15, await hrSettings.GetNextEmployeeNumberAsync());
        }
        finally
        {
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
                await SaveAndWaitForRenumberToSettleAsync(hrSettings, initialPrefix ?? "");
            }
            else
            {
                await hrSettings.SaveAsync();
            }
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
            await hrSettings.GoToAsync(BetaCorpId);
        }
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_HrSettingsPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr-settings");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        var finalUrl = _page.Url;
        while (DateTime.UtcNow < deadline)
        {
            finalUrl = _page.Url;
            if (!finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/hr-settings", StringComparison.OrdinalIgnoreCase))
                break;
            await _page.WaitForTimeoutAsync(200);
        }

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


    private static readonly Guid GraceKimEmployeeId = Guid.Parse("30000000-0000-0000-0000-000000000015");

    private async Task SaveAndWaitForRenumberToSettleAsync(HrSettingsPage hrSettings, string expectedPrefixIfRenumbered)
    {
        var renumbered = false;
        for (var attempt = 1; ; attempt++)
        {
            await hrSettings.ClickSaveAsync();
            renumbered = await hrSettings.IsRenumberDialogVisibleAsync();
            if (renumbered)
                await hrSettings.ConfirmRenumberAsync();
            await _page.WaitForSpinnerToClearAsync();

            // SET-08 rejects a format-changing save (409, nothing committed) while a previous
            // renumber for this company is still Pending/Processing — the outbox row is only marked
            // Processed just AFTER the renumbered employees are saved, and the Hangfire worker can
            // pick the job up late under load, so a prior test's (already-observed) renumber can
            // still be in flight for a moment. The form keeps its values on a rejected save, so
            // wait and resubmit, exactly as PrefixChange_ShowsRenumberDialog_... already does.
            if (!renumbered || attempt >= 8 || !await IsStillProcessingPreviousRenumberAsync())
                break;
            await _page.WaitForTimeoutAsync(10_000);
        }

        if (renumbered)
        {
            var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
            await empEdit.GoToViewAsync(BetaCorpId, GraceKimEmployeeId);

            var deadline = DateTime.UtcNow.AddSeconds(90);
            var number = await empEdit.GetEmployeeNumberFieldValueAsync();
            while (!number.StartsWith(expectedPrefixIfRenumbered, StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            {
                await _page.WaitForTimeoutAsync(3_000);
                await _page.ReloadAsync();
                number = await empEdit.GetEmployeeNumberFieldValueAsync();
            }
        }

        if (!_page.Url.Contains("/hr-settings", StringComparison.OrdinalIgnoreCase))
            await hrSettings.GoToAsync(BetaCorpId);
    }

    /// <summary>
    /// True when the just-submitted save was rejected. HrSettingsPage.razor surfaces every failed
    /// save as the same generic ".alert-danger" ("Failed to save HR settings."), so this can't read
    /// the reason; callers only consult it after a renumber-triggering save, where the realistic
    /// rejection is SET-08's "previous reformat still processing" 409.
    /// </summary>
    private async Task<bool> IsStillProcessingPreviousRenumberAsync() =>
        _page.Url.Contains("/hr-settings", StringComparison.OrdinalIgnoreCase) &&
        await _page.Locator(".alert-danger").First.IsVisibleAsync();

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
                    await _page.WaitForTimeoutAsync(15_000);
                else
                    accepted = true;
            }

            Assert.True(accepted, "The renumber-triggering save kept 409ing on a still-processing prior renumber");

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
    public async Task WorkEmailTab_IsTheSeventhTab_AndExposesAccessibleControls()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await hrSettings.GoToWorkEmailTabAsync(AcmeId);

        var tabs = _page.Locator(".content-area .e-tab-header").First.GetByRole(AriaRole.Tab);
        await Assertions.Expect(tabs).ToHaveCountAsync(7, new() { Timeout = 15_000 });
        await Assertions.Expect(tabs.Nth(6)).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("Work Email"));
        await Assertions.Expect(tabs.Nth(6)).ToHaveAttributeAsync("aria-selected", "true");

        await Assertions.Expect(hrSettings.WorkEmailEnabledCheckbox).ToBeVisibleAsync();
        await Assertions.Expect(hrSettings.WorkEmailPrimaryDomainInput).ToBeVisibleAsync();
        await Assertions.Expect(hrSettings.WorkEmailPrimaryDomainLabel).ToContainTextAsync("*");
        await Assertions.Expect(hrSettings.WorkEmailPrimaryDomainInput).Not.ToHaveValueAsync("");
        await Assertions.Expect(hrSettings.WorkEmailExample).ToHaveAttributeAsync("aria-live", "polite");
        await Assertions.Expect(hrSettings.WorkEmailSaveButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task WorkEmailSettings_ClearedPrimaryDomain_ShowsRequiredError_AndDoesNotSave_WhetherEnabledOrNot()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);
        var initiallyEnabled = await hrSettings.IsWorkEmailSuggestionsEnabledAsync();
        var initialPrimary = await hrSettings.GetWorkEmailPrimaryDomainAsync();

        try
        {
            foreach (var enabled in new[] { true, false })
            {
                await hrSettings.SetWorkEmailSuggestionsEnabledAsync(enabled);
                await hrSettings.SetWorkEmailPrimaryDomainAsync("");
                await hrSettings.SaveWorkEmailSettingsAsync();

                await Assertions.Expect(hrSettings.WorkEmailError).ToBeVisibleAsync();
                await Assertions.Expect(hrSettings.WorkEmailError).ToHaveAttributeAsync("role", "alert");
                await Assertions.Expect(hrSettings.WorkEmailError).ToContainTextAsync("Primary email domain is required");
                await Assertions.Expect(hrSettings.WorkEmailPrimaryDomainError).ToBeVisibleAsync();
                await Assertions.Expect(hrSettings.WorkEmailPrimaryDomainError).ToContainTextAsync("Primary email domain is required");
                await Assertions.Expect(hrSettings.WorkEmailSaved).ToHaveCountAsync(0);
            }
        }
        finally
        {
            await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);
            Assert.Equal(initiallyEnabled, await hrSettings.IsWorkEmailSuggestionsEnabledAsync());
            Assert.Equal(initialPrimary, await hrSettings.GetWorkEmailPrimaryDomainAsync());
        }
    }

    [Fact]
    public async Task WorkEmailSettings_ExampleUpdatesWithConvention_AndSavePersistsAfterReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);
        var initialEnabled = await hrSettings.IsWorkEmailSuggestionsEnabledAsync();
        var initialPrimary = await hrSettings.GetWorkEmailPrimaryDomainAsync();
        var initialConvention = await hrSettings.GetWorkEmailConventionAsync();

        try
        {
            await hrSettings.SetWorkEmailSuggestionsEnabledAsync(true);
            await hrSettings.SetWorkEmailPrimaryDomainAsync("@E2E-Beta.Example.com");
            await hrSettings.ExpectWorkEmailExampleAsync("jane.smith@e2e-beta.example.com");

            await hrSettings.SelectWorkEmailConventionAsync("firstinitial.lastname");
            await hrSettings.ExpectWorkEmailExampleAsync("j.smith@e2e-beta.example.com");

            await hrSettings.SelectWorkEmailConventionAsync("firstnamelastname");
            await hrSettings.ExpectWorkEmailExampleAsync("janesmith@e2e-beta.example.com");

            await hrSettings.SaveWorkEmailSettingsAsync();

            await Assertions.Expect(hrSettings.WorkEmailSaved).ToBeVisibleAsync();
            await Assertions.Expect(hrSettings.WorkEmailSaved).ToHaveAttributeAsync("role", "status");
            await Assertions.Expect(hrSettings.WorkEmailError).ToHaveCountAsync(0);

            await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);

            Assert.True(await hrSettings.IsWorkEmailSuggestionsEnabledAsync());
            Assert.Equal("e2e-beta.example.com", await hrSettings.GetWorkEmailPrimaryDomainAsync());
            Assert.Equal("firstnamelastname", await hrSettings.GetWorkEmailConventionAsync());
            await hrSettings.ExpectWorkEmailExampleAsync("janesmith@e2e-beta.example.com");
        }
        finally
        {
            await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);
            await hrSettings.ConfigureWorkEmailAsync(initialEnabled, initialPrimary, initialConvention);
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
            Assert.False(await hrSettings.HasErrorAsync(), $"Save failed: {await hrSettings.GetErrorTextAsync()}");

            await hrSettings.GoToAsync(BetaCorpId);
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");
            await hrSettings.SetEmployeeNumberPrefixAsync($"MA{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}-");
            await hrSettings.ClickSaveAsync();

            Assert.False(await hrSettings.IsRenumberDialogVisibleAsync(),
                "Manual -> Automatic must not trigger the renumber confirmation — existing numbers are left as-is");

            await _page.WaitForSpinnerToClearAsync();
            Assert.False(await hrSettings.HasErrorAsync(), $"Save failed: {await hrSettings.GetErrorTextAsync()}");

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
