using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class VacancyDetailPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/new");
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.GetByPlaceholder("e.g. Senior Software Engineer")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid vacancyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/{vacancyId}");
        await page.WaitForSelectorAsync(".e-tab, span[role='combobox']", new() { Timeout = 20_000 });
    }


    public async Task FillTitleAsync(string value)
    {
        await page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    // NOTE: Vacancy.Location was removed entirely (domain, API, UI) as part of the
    // "Vacancy - Position Profile relationship" epic's location correction — location is now
    // shown only as a read-only value derived from the linked Position Profile. The FillLocationAsync/
    // GetLocationAsync/GetLocationFieldHintAsync methods that used to live here (targeting a
    // "e.g. Remote" placeholder on this page) were removed since that field no longer exists on
    // VacancyDetail.razor; the same placeholder text is still used elsewhere by the unrelated
    // Schedule Interview dialog's free-text Location field (see SelectInterviewerAsync's sibling
    // FillScheduledAtAsync region below), which is not this page's concern.

    public async Task FillDescriptionAsync(string value)
    {
        await page.Locator("textarea.e-input").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectEmploymentTypeAsync(string typeName) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Employment Type" }).First, typeName);

    public async Task<string?> GetEmploymentTypeFieldErrorAsync() =>
        (await page.Locator("#vacancy-employment-type-error").First.TextContentAsync())?.Trim();

    public Task<bool> IsLegacyEmploymentTypeRequiredBannerVisibleAsync() =>
        page.Locator("[data-testid='vacancy-employment-type-required']").IsVisibleAsync();

    public Task SelectHiringManagerAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Hiring Manager" }).First, nameFragment);

    public async Task SelectPositionProfileAsync(string titleFragment)
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        var valueInput = group.Locator(".e-input-group input").First;
        var alreadySelected = Regex.IsMatch(await valueInput.InputValueAsync(), Regex.Escape(titleFragment));

        await DropDownSelector.SelectAsync(page, group, titleFragment);

        await Assertions.Expect(valueInput)
            .ToHaveValueAsync(new Regex(Regex.Escape(titleFragment)), new() { Timeout = 10_000 });

        if (!alreadySelected)
        {
            await page.Locator("[data-testid='position-profile-defaults-summary']")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }
    }

    public async Task OpenPositionProfileDropdownAsync()
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl:visible .e-list-item:not(.e-hide)").First.WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task<IReadOnlyList<string>> GetPositionProfileDropdownOptionsAsync()
    {
        var items = await page.Locator(".e-popup.e-ddl:visible .e-list-item:not(.e-hide)").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    public async Task<string?> GetSelectedPositionProfileTextAsync()
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task ExpectPositionProfileDisabledAsync(bool disabled)
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        var input = group.Locator("input.e-input").First;
        if (disabled)
            await Assertions.Expect(input).ToBeDisabledAsync(new() { Timeout = 10_000 });
        else
            await Assertions.Expect(input).ToBeEnabledAsync(new() { Timeout = 10_000 });
    }

    public async Task<bool> IsPositionProfileDisabledAsync()
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        return await group.Locator("input.e-input").First.IsDisabledAsync();
    }

    /// <summary>
    /// The inline "Position Profile cannot be changed after the vacancy has applications or has
    /// moved past Draft status." message shown under the Position Profile dropdown when an
    /// existing vacancy's GetVacancyResponse.CanChangePositionProfile is false (see
    /// VacancyDetail.razor's RenderDetailsCard, "Update Vacancy Details and List Screens" story).
    /// Never shown for a new vacancy (IsNew instead shows the separate "Select the position
    /// profile first…" hint).
    /// </summary>
    /// <summary>
    /// The rendered text has a conditional suffix depending on whether the viewer can request an
    /// authorised correction (VacancyDetail.razor's CanRequestCorrection) — a Recruiter sees
    /// "...has moved past Draft status, unless you request an authorised correction below.", while
    /// anyone else just sees "...has moved past Draft status." Match on the common substring
    /// rather than the full exact text so this works for either viewer.
    /// </summary>
    public Task<bool> IsPositionProfileLockedMessageVisibleAsync() =>
        page.GetByText(
            "Position Profile cannot be changed after the vacancy has applications or has moved past Draft status",
            new() { Exact = false }).IsVisibleAsync();

    private ILocator PositionProfileDefaultsSummary => page.Locator("[data-testid='position-profile-defaults-summary']");

    public Task<bool> IsPositionProfileDefaultsSummaryVisibleAsync() =>
        PositionProfileDefaultsSummary.IsVisibleAsync();

    public async Task<string?> GetSummaryDepartmentNameAsync()
    {
        var dd = PositionProfileDefaultsSummary.Locator("dt:has-text('Department') + dd");
        return (await dd.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetSummarySalaryRangeAsync()
    {
        var dd = PositionProfileDefaultsSummary.Locator("dt:has-text('Salary Range') + dd");
        return await dd.IsVisibleAsync() ? (await dd.TextContentAsync())?.Trim() : null;
    }

    // ── Authorised correction (locked Position Profile override) ────────────────
    // "Prevent Invalid Position Profile Changes" story: when the Position Profile dropdown is
    // locked (GetVacancyResponse.CanChangePositionProfile == false) and the current user is a
    // Recruiter, an "authorised correction" section (data-testid=
    // "position-profile-correction-section") appears below the locked-dropdown message inside the
    // "Position Profile" card, letting the dropdown be re-enabled once a Correction Reason is
    // supplied. See VacancyDetail.razor's RenderDetailsCard /
    // OnAuthorisedCorrectionToggled/CanRequestCorrection.

    private ILocator CorrectionSection => page.Locator("[data-testid='position-profile-correction-section']");

    public Task<bool> IsCorrectionSectionVisibleAsync() => CorrectionSection.IsVisibleAsync();

    private ILocator CorrectionCheckbox => page.Locator("#isAuthorisedCorrection");

    public Task<bool> IsCorrectionCheckboxVisibleAsync() => CorrectionCheckbox.IsVisibleAsync();

    public Task<bool> IsCorrectionCheckboxCheckedAsync() => CorrectionCheckbox.IsCheckedAsync();

    /// <summary>
    /// Checks or unchecks the "This is an authorised correction" checkbox, waiting on the
    /// checked-state to settle (the Blazor @onchange handler — OnAuthorisedCorrectionToggled —
    /// re-renders the Correction Reason field and re-enables/disables the Position Profile
    /// dropdown synchronously, but a brief wait keeps this robust against render timing).
    /// </summary>
    public async Task SetAuthorisedCorrectionCheckedAsync(bool value)
    {
        if (value)
            await CorrectionCheckbox.CheckAsync();
        else
            await CorrectionCheckbox.UncheckAsync();

        // The doc comment above promises a settle wait against OnAuthorisedCorrectionToggled's
        // re-render, but none previously existed — Check/UncheckAsync only guarantee the native
        // input's checked state flipped, not that the Blazor circuit has processed the resulting
        // @onchange and re-rendered the Correction Reason field / Position Profile dropdown yet.
        // Wait for the reason field to actually match the expected reveal/hide state before
        // returning so callers don't race that re-render.
        await CorrectionReasonInput.WaitForAsync(new()
        {
            State = value ? WaitForSelectorState.Visible : WaitForSelectorState.Hidden,
            Timeout = 15_000,
        });
        await ExpectPositionProfileDisabledAsync(!value);
    }

    /// <summary>
    /// The "Correction Reason" required text field (HrTextBox bound to Model.CorrectionReason),
    /// only rendered while the authorised-correction checkbox is checked. Located by its
    /// placeholder, matching this file's existing HrTextBox locator convention (see
    /// FillTitleAsync's doc comment).
    /// </summary>
    private ILocator CorrectionReasonInput => page.GetByPlaceholder("Explain why this correction is required");

    public Task<bool> IsCorrectionReasonFieldVisibleAsync() => CorrectionReasonInput.IsVisibleAsync();

    public async Task FillCorrectionReasonAsync(string value)
    {
        await CorrectionReasonInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }


    private ILocator LinkedPositionProfileCard => page.Locator("[data-testid='linked-position-profile-card']");

    public async Task<bool> IsLinkedPositionProfileCardVisibleAsync()
    {
        try
        {
            await LinkedPositionProfileCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            return false;
        }

        return await LinkedPositionProfileCard.IsVisibleAsync();
    }

    public async Task<string?> GetLinkedPositionProfileTitleAsync()
    {
        var span = LinkedPositionProfileCard.Locator("span.fw-semibold");

        await span.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        return await span.IsVisibleAsync() ? (await span.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetLinkedPositionProfileDepartmentAsync()
    {
        var dd = LinkedPositionProfileCard.Locator("dt:has-text('Department') + dd");
        return (await dd.TextContentAsync())?.Trim();
    }

    public Task<bool> IsLinkedPositionProfileInactiveBadgeVisibleAsync() =>
        LinkedPositionProfileCard.GetByText("Inactive", new() { Exact = true }).IsVisibleAsync();

    public async Task<string?> GetLinkedPositionProfileEmptyMessageAsync()
    {
        var p = LinkedPositionProfileCard.Locator("p.text-muted");
        return await p.IsVisibleAsync() ? (await p.TextContentAsync())?.Trim() : null;
    }

    public async Task<bool> IsViewPositionProfileLinkVisibleAsync()
    {
        var link = LinkedPositionProfileCard.GetByRole(AriaRole.Link, new() { Name = "View Position Profile" });

        try
        {
            await link.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            return false;
        }

        return await link.IsVisibleAsync();
    }

    public Task ClickViewPositionProfileLinkAsync() =>
        LinkedPositionProfileCard.GetByRole(AriaRole.Link, new() { Name = "View Position Profile" }).ClickAsync();

    public Task<bool> HasRecruitmentAdvertDetailsHeaderAsync() =>
        page.Locator(".card-header h5").Filter(new() { HasText = "Recruitment Advert Details" }).IsVisibleAsync();

    private ILocator RecruitmentAdvertDetailsCard =>
        page.Locator(".card").Filter(new() { Has = page.Locator(".card-header h5:has-text('Recruitment Advert Details')") });

    public Task<int> CountDepartmentFieldsInAdvertDetailsCardAsync() =>
        RecruitmentAdvertDetailsCard.Locator("label.form-label", new() { HasText = "Department" }).CountAsync();

    public async Task<bool> HasAdvertTitleLabelAsync()
    {
        try
        {
            await RecruitmentAdvertDetailsCard.GetByText("Advert Title", new() { Exact = true }).WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> HasAdvertDescriptionLabelAsync()
    {
        try
        {
            await RecruitmentAdvertDetailsCard.GetByText("Advert Description", new() { Exact = true }).WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> HasOptionalSuffixAsync() =>
        await RecruitmentAdvertDetailsCard.GetByText("(optional)", new() { Exact = false }).CountAsync() > 0;

    public async Task<string?> GetHeaderTextAsync()
    {
        var h1 = page.Locator("h1.fs-4");
        await h1.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return (await h1.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = page.Locator(".status-badge").First;
        return (await badge.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetSelectedHiringManagerTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Hiring Manager" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task SaveNewVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task SaveExistingVacancyAsync() => SaveNewVacancyAsync();

    /// <summary>
    /// Clicks the Overview tab's "Save" button without waiting for navigation — for tests that
    /// expect client- or server-side validation to keep the form on the page (e.g. an authorised
    /// correction submitted with an empty Correction Reason; see UpdateVacancyValidator). Compare
    /// <see cref="SaveExistingVacancyAsync"/>, which waits for the post-save redirect to the list.
    /// </summary>
    public Task ClickSaveButtonAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

    // ── "Advertise this vacancy to employees" checkbox (Internal Vacancies feature) ──
    // SfCheckBox ID="isAdvertisedInternally" bound to Model.IsAdvertisedInternally, in the
    // "Recruitment Advert Details" card (data-testid="vacancy-advertise-internally"). Drive it by
    // its input ID — same pattern as the authorised-correction checkbox above (SfCheckBox forwards
    // its ID onto the underlying input; Check/UncheckAsync handle the Syncfusion visual wrapper).
    private ILocator AdvertiseInternallyCheckbox => page.Locator("#isAdvertisedInternally");

    public Task<bool> IsAdvertiseInternallyCheckedAsync() => AdvertiseInternallyCheckbox.IsCheckedAsync();

    public async Task SetAdvertiseInternallyAsync(bool value)
    {
        await AdvertiseInternallyCheckbox.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        if (value)
            await AdvertiseInternallyCheckbox.CheckAsync();
        else
            await AdvertiseInternallyCheckbox.UncheckAsync();
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public Task<string> GetTitleAsync() =>
        page.GetByPlaceholder("e.g. Senior Software Engineer").InputValueAsync();

    public Guid GetIdFromUrl() => UrlIdParser.LastGuid(page.Url);

    public async Task SetAdvertTitleAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. Senior Software Engineer");
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<string> WaitForAdvertTitleAsync(string expected)
    {
        var input = page.GetByPlaceholder("e.g. Senior Software Engineer");
        await Assertions.Expect(input).ToHaveValueAsync(expected, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks the Overview tab's Save on an existing vacancy and waits for the optimistic-concurrency
    /// banner — i.e. the save was rejected (HTTP 409) because the vacancy changed since this form
    /// loaded it. A conflicted save stays on the edit page (no navigation to the list).
    /// </summary>
    public async Task SaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }


    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Cancel|Back to vacancies)$") }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard changes" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Stay on page" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }


    public async Task<bool> IsPublishButtonVisibleAsync()
    {
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Publish Vacancy" }).WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task PublishVacancyAsync()
    {
        var publishButton = page.GetByRole(AriaRole.Button, new() { Name = "Publish Vacancy" });
        try
        {
            await publishButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            await page.ReloadAsync();
            await page.WaitForSelectorAsync(".e-tab, span[role='combobox']", new() { Timeout = 20_000 });
            try
            {
                await publishButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            }
            catch (TimeoutException ex)
            {
                var alerts = string.Join(" | ", await page.Locator(".alert").AllInnerTextsAsync());
                var heading = await page.Locator("h1").First.InnerTextAsync();
                var badge = await page.Locator(".vacancy-header-actions").First.InnerTextAsync();
                throw new InvalidOperationException(
                    $"Publish Vacancy button not shown at {page.Url}. Heading: '{heading}'. Header actions: '{badge}'. Alerts: '{alerts}'.", ex);
            }
        }
        await publishButton.ClickAsync();
        await Assertions.Expect(publishButton).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }


    public Task<bool> HasTabAsync(string name) =>
        page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true }).IsVisibleAsync();

    private async Task OpenVacancyTabAsync(string tabName, string contentTestId)
    {
        var tab = page.GetByRole(AriaRole.Tab, new() { Name = tabName });

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await tab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                break;
            }
            catch (TimeoutException ex) when (attempt >= 3)
            {
                var tabs = string.Join(" | ", await page.GetByRole(AriaRole.Tab).AllInnerTextsAsync());
                var heading = await page.Locator("h1").First.InnerTextAsync();
                var alerts = string.Join(" | ", await page.Locator(".alert").AllInnerTextsAsync());
                throw new InvalidOperationException(
                    $"Vacancy tab '{tabName}' never appeared at {page.Url}. Heading: '{heading}'. Tabs: '{tabs}'. Alerts: '{alerts}'.", ex);
            }
            catch (TimeoutException)
            {
                await page.ReloadAsync();
                await page.WaitForSelectorAsync(".e-tab", new() { Timeout = 20_000 });
            }
        }

        await tab.ClickAsync();
        await page.WaitForSelectorAsync($"[data-testid='{contentTestId}']", new() { Timeout = 15_000 });
    }

    public Task OpenApplicationsTabAsync() => OpenVacancyTabAsync("Applications", "vacancy-applications-tab");

    public Task OpenInterviewsTabAsync() => OpenVacancyTabAsync("Interviews", "vacancy-interviews-tab");


    public async Task ClickAddCandidateAsync()
    {
        await page.Locator("[data-testid='add-application-btn']").ClickAsync();
        await page.Locator("[role='dialog'].add-application-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task SelectCandidateInAddDialogAsync(string nameOrEmailFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-application-dialog"), nameOrEmailFragment);

    public Task SelectAddApplicationSourceAsync(string sourceLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-application-dialog"), sourceLabel, index: 1);

    public async Task SelectAddApplicationRecruiterAsync(string agencyNameFragment)
    {
        var recruiterGroup = page.Locator(".add-application-dialog .mb-3").Filter(new() { HasText = "Recruiter" });
        await recruiterGroup.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await DropDownSelector.SelectAsync(page, recruiterGroup, agencyNameFragment);
    }

    public async Task<bool> IsRecruiterNotAssignedWarningVisibleAsync()
    {
        try
        {
            await page.Locator("[data-testid='recruiter-not-assigned-warning']").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task SubmitAddApplicationAsync()
    {
        await page.Locator(".add-application-dialog .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog'].add-application-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public Task ClickAddApplicationSubmitButtonAsync() =>
        page.Locator(".add-application-dialog .e-footer-content button:has-text('Add')").ClickAsync();

    public async Task<string?> GetApplicationSourceColumnTextAsync(string candidateNameFragment)
    {
        var cell = ApplicationRow(candidateNameFragment).First.Locator(".e-rowcell").Nth(5);
        return (await cell.TextContentAsync())?.Trim();
    }

    // ── Applications tab: row selection + toolbar actions ───────────────────────
    // Row-level actions (Schedule Interview/Offer/Hire/Reject/Withdraw) moved from a per-row
    // Actions column into the grid's own toolbar (see VacancyApplicationsTab.razor's GridToolbar/
    // OnToolbarClick) — a caller now has to select the row first (click anywhere in it, which
    // fires RowSelected and enables the toolbar buttons for that row), then click the toolbar
    // button by its accessible name, rather than clicking a button embedded in the row itself.

    private ILocator ApplicationsTab => page.Locator("[data-testid='vacancy-applications-tab']");

    private ILocator ApplicationRow(string candidateNameFragment) =>
        ApplicationsTab.Locator(".e-grid .e-row").Filter(new() { HasText = candidateNameFragment });

    private ILocator ApplicationStageBadge(string candidateNameFragment) =>
        ApplicationRow(candidateNameFragment).First.Locator("[data-testid='application-stage-badge'] .badge");

    // Web-first grid expectations (internal recruitment Ticket 3). After a successful Add the tab
    // re-runs LoadAsync (loading indicator, then a fresh grid, then the lazily-fetched Source
    // details), so these retry until the post-reload DOM matches rather than reading a snapshot.

    public Task ExpectApplicationRowCountAsync(string candidateNameFragment, int expectedCount) =>
        Assertions.Expect(ApplicationRow(candidateNameFragment)).ToHaveCountAsync(expectedCount, new() { Timeout = 30_000 });

    public Task ExpectApplicationStatusAsync(string candidateNameFragment, string expectedStage) =>
        Assertions.Expect(ApplicationStageBadge(candidateNameFragment))
            .ToHaveTextAsync(expectedStage, new() { Timeout = 30_000 });

    public Task ExpectApplicationSourceAsync(string candidateNameFragment, string expectedText) =>
        Assertions.Expect(ApplicationRow(candidateNameFragment).First.Locator(".e-rowcell").Nth(5))
            .ToContainTextAsync(expectedText, new() { Timeout = 30_000 });

    public Task ExpectActionSuccessMessageAsync(string expectedText) =>
        Assertions.Expect(ApplicationsTab.Locator(".alert-success").First)
            .ToContainTextAsync(expectedText, new() { Timeout = 30_000 });

    public async Task<string?> GetApplicationRowTextAsync(string candidateNameFragment)
    {
        var row = ApplicationRow(candidateNameFragment).First;
        return await row.IsVisibleAsync() ? (await row.TextContentAsync())?.Trim() : null;
    }

    public async Task ClickReviewCvForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        var button = await ApplicationsToolbarButtonAsync("Review CV", reselectCandidateNameFragment: candidateNameFragment);
        await button.ClickUntilUrlAsync(page, u => u.Contains("/review-cv"));
    }

    public async Task<string?> GetApplicationStatusAsync(string candidateNameFragment)
    {
        var badge = ApplicationStageBadge(candidateNameFragment);
        try
        {
            await badge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await badge.TextContentAsync())?.Trim();
    }

    private ILocator SelectionMarker => ApplicationsTab.Locator("[data-testid='selected-application-marker']");

    private async Task SelectApplicationRowAsync(string candidateNameFragment)
    {
        var row = ApplicationRow(candidateNameFragment).First;
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var applicationId = await row.GetAttributeAsync("data-application-id")
            ?? throw new InvalidOperationException(
                $"Application row for '{candidateNameFragment}' has no data-application-id attribute.");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (await SelectionMarker.GetAttributeAsync("data-selected-application-id") == applicationId)
                return;

            await row.Locator(".e-rowcell").Nth(1).ClickAsync();
            try
            {
                await Assertions.Expect(SelectionMarker)
                    .ToHaveAttributeAsync("data-selected-application-id", applicationId, new() { Timeout = 10_000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
            }
        }
    }

    private async Task ReselectApplicationRowAsync(string candidateNameFragment)
    {
        var row = ApplicationRow(candidateNameFragment).First;
        var applicationId = await row.GetAttributeAsync("data-application-id");
        if (applicationId is not null &&
            await SelectionMarker.GetAttributeAsync("data-selected-application-id") == applicationId)
        {
            await row.Locator(".e-rowcell").Nth(1).ClickAsync();
            await Assertions.Expect(SelectionMarker)
                .ToHaveAttributeAsync("data-selected-application-id", "", new() { Timeout = 10_000 });
        }
        await SelectApplicationRowAsync(candidateNameFragment);
    }

    /// <summary>
    /// Returns the (enabled/disabled) toolbar button with the given accessible name within the
    /// Applications tab's own grid toolbar — scoped there so this can't collide with any other
    /// toolbar/button of the same name elsewhere on the page. With five items (Schedule Interview/
    /// Offer/Hire/Reject/Withdraw) the EJ2 grid toolbar can overflow into an ".e-toolbar-pop" popup
    /// on narrower viewports, pushing later items (Withdraw especially) out of ".e-toolbar" — if the
    /// button isn't directly visible there, open the overflow popup ("..." nav button) and look
    /// inside it instead.
    /// </summary>
    private async Task<ILocator> ApplicationsToolbarButtonAsync(
        string name, bool exact = false, string? reselectCandidateNameFragment = null)
    {
        var direct = ApplicationsTab.Locator(".e-toolbar").GetByRole(AriaRole.Button, new() { Name = name, Exact = exact });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await direct.IsVisibleAsync())
                break;
            await page.WaitForTimeoutAsync(200);
        }

        ILocator resolved;
        if (await direct.IsVisibleAsync())
        {
            resolved = direct;
        }
        else
        {
            var overflowToggle = ApplicationsTab.Locator(".e-toolbar .e-nav-right, .e-toolbar .e-hscroll-bar .e-nav-right");
            if (await overflowToggle.CountAsync() == 0)
            {
                await direct.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                resolved = direct;
            }
            else
            {
                await overflowToggle.ClickAsync();
                var popup = page.Locator(".e-toolbar-pop:visible").GetByRole(AriaRole.Button, new() { Name = name, Exact = exact });
                await popup.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
                resolved = popup;
            }
        }

        if (reselectCandidateNameFragment is null)
            return resolved;

        var enableDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < enableDeadline)
        {
            if (await resolved.GetAttributeAsync("aria-disabled") != "true")
                return resolved;
            await page.WaitForTimeoutAsync(200);
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (await resolved.GetAttributeAsync("aria-disabled") != "true")
                return resolved;

            await ReselectApplicationRowAsync(reselectCandidateNameFragment);

            var reDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < reDeadline)
            {
                if (await resolved.GetAttributeAsync("aria-disabled") != "true")
                    return resolved;
                await page.WaitForTimeoutAsync(200);
            }
        }

        return resolved;
    }

    public async Task<bool> HasAnyPerRowApplicationActionButtonAsync()
    {
        var rows = ApplicationsTab.Locator(".e-grid .e-row");
        var count = await rows.Locator("button").CountAsync();
        return count > 0;
    }

    public async Task ClickScheduleInterviewForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Schedule Interview", reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
    }

    public async Task ClickOfferForAsync(string candidateNameFragment, InternalOfferTerms? internalOffer = null)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Offer", exact: true, reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
        await CompleteOfferDialogAsync(internalOffer);
    }

    public async Task<InternalOfferDialog> OpenReviseOfferDialogAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Revise Offer", exact: true, reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
        var dialog = new InternalOfferDialog(page);
        await dialog.WaitForOpenAsync();
        return dialog;
    }

    public async Task<InternalOfferDialog> OpenInternalOfferDialogAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Offer", exact: true, reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
        var dialog = new InternalOfferDialog(page);
        await dialog.WaitForOpenAsync();
        return dialog;
    }

    private async Task CompleteOfferDialogAsync(InternalOfferTerms? internalOffer)
    {
        var offerDialog = new InternalOfferDialog(page);
        await offerDialog.WaitForOpenAsync();
        if (await offerDialog.IsInternalAsync())
            await offerDialog.FillAsync(internalOffer ?? new InternalOfferTerms());
        await offerDialog.SubmitExpectingSuccessAsync();
    }

    public Task ExpectOfferResponseBadgeAsync(string candidateNameFragment, string expectedText) =>
        Assertions.Expect(ApplicationRow(candidateNameFragment).First.Locator("[data-testid='offer-response-badge']"))
            .ToContainTextAsync(expectedText, new() { Timeout = 30_000 });

    public async Task ExpectToolbarItemDisabledForRowAsync(string candidateNameFragment, string itemText)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButtonAsync(itemText, exact: true);
        await ExpectToolbarItemDisabledAsync(itemText);
    }

    public async Task ClickRejectForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Reject", reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
    }

    public async Task ClickWithdrawForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Withdraw", reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='vacancy-applications-tab'] .alert-success"))
            .ToHaveTextAsync("Application withdrawn.", new() { Timeout = 10_000 });
    }

    public async Task ReachAcceptedOfferAsync(
        string candidateNameFragment, string interviewerFragment = "James", InternalOfferTerms? internalOffer = null)
    {
        await ClickScheduleInterviewForAsync(candidateNameFragment);
        await WaitForScheduleDialogAsync();
        await SelectInterviewerAsync(interviewerFragment);
        await FillScheduledAtAsync("01/09/2099 10:00");
        await SubmitScheduleInterviewAsync();

        await OpenInterviewsTabAsync();
        await ClickRecordOutcomeForAsync(candidateNameFragment);
        await WaitForOutcomeDialogAsync();
        await SelectOutcomeAsync("Passed");
        await SubmitOutcomeAsync();

        await OpenApplicationsTabAsync();
        await ClickOfferForAsync(candidateNameFragment, internalOffer);

        await OpenRecordOfferResponseDialogAsync(candidateNameFragment);
        await SelectOfferResponseStatusAsync("Accepted");
        await SubmitOfferResponseAsync();
    }

    public async Task ClickHireForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Hire", reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
    }

    public async Task<bool> IsApplicationsToolbarButtonEnabledAsync(string candidateNameFragment, string buttonName, bool exact = false)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        var button = await ApplicationsToolbarButtonAsync(buttonName, exact);
        await page.WaitForTimeoutAsync(500);
        return await button.GetAttributeAsync("aria-disabled") != "true";
    }


    public async Task WaitForScheduleDialogAsync() =>
        await page.Locator("[role='dialog'].schedule-interview-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    public Task SelectInterviewerAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".schedule-interview-dialog"), nameFragment);

    public async Task FillScheduledAtAsync(string ddMMyyyyHHmm)
    {
        var group = page.Locator(".schedule-interview-dialog .mb-3").Filter(new() { HasText = "Scheduled At" });
        var input = group.Locator("input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyyHHmm);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitScheduleInterviewAsync()
    {
        await page.Locator(".schedule-interview-dialog .e-footer-content button:has-text('Schedule')").ClickAsync();
        await page.Locator("[role='dialog'].schedule-interview-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForTimeoutAsync(250);
    }

    // ── Reject dialog ────────────────────────────────────────────────────────────

    public async Task SubmitRejectAsync()
    {
        await page.Locator(".reject-candidate-dialog .e-footer-content button:has-text('Reject')").ClickAsync();
        await page.Locator("[role='dialog'].reject-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }


    public async Task WaitForHireDialogAsync() =>
        await page.Locator("[role='dialog'].hire-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    public async Task FillHireStartDateAsync(string ddMMyyyy)
    {
        var input = page.Locator(".hire-candidate-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillHireDateOfBirthAsync(string ddMMyyyy)
    {
        var input = page.Locator(".hire-candidate-dialog .e-date-wrapper input.e-input").Nth(1);
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectHireNationalityAsync(string nationality) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hire-candidate-dialog"), nationality);

    public Task SelectHireGenderAsync(string gender) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hire-candidate-dialog"), gender, index: 1);

    public async Task FillHireEmployeeNumberAsync(string value)
    {
        var dialog = page.Locator(".hire-candidate-dialog");
        var field = dialog.GetByPlaceholder("e.g. EMP-001");
        var autoAssignedMessage = dialog.Locator(".hire-derived-value")
            .Filter(new() { HasText = "Assigned automatically" });

        await field.Or(autoAssignedMessage).First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        if (await field.IsVisibleAsync())
        {
            await field.FillAsync(value);
            await page.Keyboard.PressAsync("Tab");
        }
    }

    public async Task FillHireAddressAsync(string addressLine1, string city, string postCode)
    {
        await FillHireTextAsync("hire-address-line-1", addressLine1);
        await FillHireTextAsync("hire-city", city);
        await FillHireTextAsync("hire-post-code", postCode);
    }

    public async Task FillHireTextAsync(string fieldId, string value)
    {
        var input = page.Locator($".hire-candidate-dialog #{fieldId}");
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string?> GetHireFieldAriaRequiredAsync(string fieldId) =>
        page.Locator($".hire-candidate-dialog #{fieldId}").GetAttributeAsync("aria-required");

    public async Task<string?> GetHireFieldErrorAsync(string fieldId) =>
        (await page.Locator($".hire-candidate-dialog #{fieldId}-error").First.TextContentAsync())?.Trim();

    public async Task<string?> GetHireDerivedEmploymentTypeTextAsync() =>
        (await page.Locator("[data-testid='hire-derived-employment-type']").TextContentAsync())?.Trim();

    public Task<bool> IsHireEmploymentTypeRequiredVisibleAsync() =>
        page.Locator("[data-testid='hire-employment-type-required']").IsVisibleAsync();

    public async Task<string?> GetHireManagerHelpTextAsync() =>
        (await page.Locator("[data-testid='hire-manager-help']").TextContentAsync())?.Trim();

    public async Task<string?> GetSelectedHireManagerTextAsync()
    {
        var group = page.Locator(".hire-candidate-dialog .col-md-6").Filter(new() { Has = page.Locator("#hire-manager-label") }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public Task SelectHireManagerAsync(string optionText) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator(".hire-candidate-dialog .col-md-6").Filter(new() { Has = page.Locator("#hire-manager-label") }).First,
            optionText);

    public async Task<string?> GetSelectedHireNationalityTextAsync()
    {
        var group = page.Locator(".hire-candidate-dialog .col-md-6").Filter(new() { HasText = "Nationality" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public Task SelectHireDropdownAsync(string labelText, string optionText) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator(".hire-candidate-dialog .col-md-6").Filter(new() { HasText = labelText }).First,
            optionText);

    public async Task<string?> GetSelectedHireDropdownTextAsync(string labelText)
    {
        var group = page.Locator(".hire-candidate-dialog .col-md-6")
            .Filter(new() { HasText = labelText })
            .First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task<string?> GetHireDerivedPositionProfileTextAsync() =>
        (await page.Locator("[data-testid='hire-derived-position-profile']").TextContentAsync())?.Trim();

    public async Task<string?> GetHireDerivedLocationTextAsync() =>
        (await page.Locator("[data-testid='hire-derived-location']").TextContentAsync())?.Trim();

    public async Task<bool> HasHireDropdownLabelAsync(string labelText) =>
        await page.Locator(".hire-candidate-dialog .col-md-6")
            .Filter(new() { HasText = labelText })
            .Locator("span[role='combobox']")
            .CountAsync() > 0;

    public async Task SubmitHireAsync()
    {
        await page.Locator(".hire-candidate-dialog .e-footer-content button:has-text('Hire')").ClickAsync();
        await page.Locator("[role='dialog'].hire-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    public Task ClickHireSubmitButtonAsync() =>
        page.Locator(".hire-candidate-dialog .e-footer-content button:has-text('Hire')").ClickAsync();

    public async Task CancelHireDialogAsync()
    {
        await page.Locator(".hire-candidate-dialog .e-footer-content button:has-text('Cancel')").ClickAsync();
        await page.Locator("[role='dialog'].hire-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<bool> HasDialogErrorAsync(string dialogCssClass) =>
        await page.Locator($".{dialogCssClass} .alert-danger").IsVisibleAsync();

    private ILocator RecruitmentAgencyField =>
        page.Locator(".col-md-4").Filter(new() { HasText = "Recruitment Agency" }).First;

    public Task SelectRecruitmentAgencyAsync(string agencyNameFragment) =>
        DropDownSelector.SelectAsync(page, RecruitmentAgencyField, agencyNameFragment);

    public async Task<string?> GetSelectedRecruitmentAgencyTextAsync()
    {
        var input = RecruitmentAgencyField.Locator(".e-input-group input").First;
        return await input.InputValueAsync();
    }

    public async Task OpenRecruitmentAgencyDropdownAsync()
    {
        await RecruitmentAgencyField.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
    }

    public async Task<IReadOnlyList<string>> GetRecruitmentAgencyDropdownOptionsAsync()
    {
        var items = await page.Locator(".e-popup.e-ddl:visible .e-list-item:not(.e-hide)").AllAsync();
        var names = new List<string>();
        foreach (var item in items)
            names.Add((await item.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<string?> GetActionSuccessMessageAsync()
    {
        var alert = page.Locator("[data-testid='vacancy-applications-tab'] .alert-success").First;
        return await alert.IsVisibleAsync() ? (await alert.TextContentAsync())?.Trim() : null;
    }


    private ILocator InterviewRow(string candidateNameFragment) =>
        page.Locator("[data-testid='vacancy-interviews-tab'] .e-grid .e-row").Filter(new() { HasText = candidateNameFragment });

    public async Task<string?> GetInterviewOutcomeAsync(string candidateNameFragment)
    {
        var badge = InterviewRow(candidateNameFragment).First.Locator(".badge").First;
        try
        {
            await badge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await badge.TextContentAsync())?.Trim();
    }

    public Task ClickRecordOutcomeForAsync(string candidateNameFragment) =>
        InterviewRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Record Outcome" }).ClickAsync();

    public async Task WaitForOutcomeDialogAsync() =>
        await page.Locator("[role='dialog'].record-outcome-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    public Task SelectOutcomeAsync(string outcome) =>
        DropDownSelector.SelectAsync(page, page.Locator(".record-outcome-dialog"), outcome);

    public async Task SubmitOutcomeAsync()
    {
        await page.Locator(".record-outcome-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].record-outcome-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }


    private ILocator OfferDialog => page.Locator("[role='dialog'].offer-candidate-dialog");

    public async Task OpenMakeOfferDialogAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Offer", exact: true, candidateNameFragment)).ClickAsync();
        await OfferDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string?> GetOfferPositionProfileContextTextAsync()
    {
        var ctx = page.Locator("[data-testid='offer-position-profile-context']");
        await ctx.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        return (await ctx.TextContentAsync())?.Trim();
    }

    private ILocator OfferSalaryInput =>
        page.Locator(".offer-candidate-dialog .col-md-6").Filter(new() { HasText = "Offered Salary" })
            .Locator("input.e-numerictextbox, input.e-input").First;

    public async Task<string> GetOfferedSalaryValueAsync()
    {
        await page.Locator(".offer-candidate-dialog .e-dlg-header-content").ClickAsync();
        await Assertions.Expect(OfferSalaryInput).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex(@"\d,\d{3}"), new() { Timeout = 15_000 });
        return await OfferSalaryInput.InputValueAsync();
    }

    public async Task SetOfferedSalaryAsync(string value)
    {
        var expected = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        await Assertions.Expect(OfferSalaryInput).ToBeEnabledAsync(new() { Timeout = 30_000 });

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await OfferSalaryInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await page.WaitForTimeoutAsync(150);
            await OfferSalaryInput.PressSequentiallyAsync(value, new() { Delay = 30 });
            await page.Keyboard.PressAsync("Tab");

            var committed = await OfferSalaryInput.InputValueAsync();
            var numeric = new string(committed.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (decimal.TryParse(numeric, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var actual) && actual == expected)
                return;

            await page.WaitForTimeoutAsync(250);
        }

        throw new PlaywrightException(
            $"Offered Salary did not commit '{value}' after 3 attempts (field shows '{await OfferSalaryInput.InputValueAsync()}').");
    }

    public Task SelectOfferSalaryFrequencyAsync(string frequency) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator(".offer-candidate-dialog .col-md-6").Filter(new() { HasText = "Salary Frequency" }),
            frequency);

    private ILocator OfferDateField(string labelText) =>
        page.Locator(".offer-candidate-dialog .col-md-6").Filter(new() { HasText = labelText })
            .Locator("input.e-input").First;

    public async Task FillOfferProposedStartDateAsync(string ddMMyyyy)
    {
        var input = OfferDateField("Proposed Start Date");
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillOfferDateAsync(string ddMMyyyy)
    {
        var input = OfferDateField("Offer Date");
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillOfferNotesAsync(string value)
    {
        var textarea = page.Locator(".offer-candidate-dialog textarea#offer-notes");
        await textarea.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitOfferAsync()
    {
        await page.Locator(".offer-candidate-dialog .e-footer-content button:has-text('Make Offer')").ClickAsync();
        await OfferDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await Assertions.Expect(page.Locator("[data-testid='vacancy-applications-tab'] .alert-success"))
            .ToHaveTextAsync("Offer made to candidate.", new() { Timeout = 15_000 });
    }

    public Task ClickMakeOfferButtonAsync() =>
        page.Locator(".offer-candidate-dialog .e-footer-content button:has-text('Make Offer')").ClickAsync();


    private ILocator OfferResponseDialog => page.Locator("[role='dialog'].offer-response-dialog");

    public async Task<bool> IsRecordOfferResponseToolbarItemEnabledAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        var item = ApplicationsTab.Locator(".e-toolbar-item").Filter(new() { HasText = "Record Offer Response" }).First;
        await item.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });

        bool enabled = false;
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            var cls = await item.GetAttributeAsync("class") ?? "";
            enabled = !cls.Contains("e-overlay") && !cls.Contains("e-disabled");
            if (enabled) break;
            await page.WaitForTimeoutAsync(200);
        }
        return enabled;
    }

    public async Task OpenRecordOfferResponseDialogAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Record Offer Response", exact: true, reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
        await OfferResponseDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public Task SelectOfferResponseStatusAsync(string status) =>
        DropDownSelector.SelectAsync(page, page.Locator(".offer-response-dialog"), status);

    public async Task SubmitOfferResponseAsync()
    {
        await page.Locator(".offer-response-dialog .e-footer-content button:has-text('Record Response')").ClickAsync();
        await OfferResponseDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<string?> GetOfferResponseBadgeTextAsync(string candidateNameFragment)
    {
        var badge = ApplicationRow(candidateNameFragment).First.Locator("[data-testid='offer-response-badge']");
        try
        {
            await badge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await badge.TextContentAsync())?.Trim();
    }


    public async Task<string?> GetHireOfferAcceptedContextTextAsync()
    {
        var el = page.Locator("[data-testid='hire-offer-accepted-context']");
        try
        {
            await el.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await el.TextContentAsync())?.Trim();
    }

    public async Task<bool> IsHireOfferBlockedWarningVisibleAsync()
    {
        try
        {
            await page.Locator("[data-testid='hire-offer-blocked']").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string> GetHireStartDateValueAsync()
    {
        var input = page.Locator(".hire-candidate-dialog .e-date-wrapper input.e-input").First;
        await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        return await input.InputValueAsync();
    }

    // ── Internal recruitment Ticket 6: Internal badge + Application type filter ──
    // Rows are addressed by their stable data-application-id (set in VacancyApplicationsTab.razor's
    // OnApplicationRowDataBound), never by position or by the badge's CSS class alone. All of these
    // are web-first expectations: the filter change re-runs LoadAsync (loading indicator, then a
    // fresh grid), so they retry until the post-reload DOM matches.

    private ILocator ApplicationRows => ApplicationsTab.Locator("tr[data-testid='application-row']");

    private ILocator ApplicationRowById(Guid applicationId) =>
        ApplicationsTab.Locator($"tr[data-testid='application-row'][data-application-id='{applicationId}']");

    private ILocator ApplicationTypeFilter => ApplicationsTab.Locator("[data-testid='application-type-filter']");

    public Task ExpectApplicationRowTotalAsync(int expectedCount) =>
        Assertions.Expect(ApplicationRows).ToHaveCountAsync(expectedCount, new() { Timeout = 30_000 });

    public Task ExpectApplicationRowVisibleAsync(Guid applicationId) =>
        Assertions.Expect(ApplicationRowById(applicationId)).ToBeVisibleAsync(new() { Timeout = 30_000 });

    public Task ExpectApplicationRowAbsentAsync(Guid applicationId) =>
        Assertions.Expect(ApplicationRowById(applicationId)).ToHaveCountAsync(0, new() { Timeout = 30_000 });

    public async Task ExpectApplicationRowInternalAsync(Guid applicationId, bool isInternal)
    {
        var row = ApplicationRowById(applicationId);
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(row).ToHaveAttributeAsync("data-internal", isInternal ? "true" : "false", new() { Timeout = 15_000 });

        var cell = row.Locator("[data-testid='application-candidate-cell']");
        await Assertions.Expect(cell.Locator("[data-testid='application-candidate-link']")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var badge = cell.Locator("[data-testid='internal-application-badge']");
        if (isInternal)
        {
            await Assertions.Expect(badge).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(badge).ToHaveTextAsync("Internal");
            await Assertions.Expect(badge).ToHaveAttributeAsync("title", "Internal applicant (current employee)");
        }
        else
        {
            await Assertions.Expect(badge).ToHaveCountAsync(0);
        }
    }

    public Task ExpectApplicationTypeFilterValueAsync(string label) =>
        Assertions.Expect(ApplicationTypeFilter.Locator("span[role='combobox'] input").First)
            .ToHaveValueAsync(label, new() { Timeout = 15_000 });

    public Task SelectApplicationTypeFilterAsync(string label) =>
        DropDownSelector.SelectAsync(page, ApplicationTypeFilter, label);

    public Task ExpectApplicationsEmptyAsync(string expectedText) =>
        Assertions.Expect(ApplicationsTab.Locator("[data-testid='applications-empty']"))
            .ToContainTextAsync(expectedText, new() { Timeout = 30_000 });

    // ── Internal recruitment Ticket 7: Appoint (internal) vs Hire (external) ──────
    // Toolbar items "app-appoint" ("Appoint") and "app-hire" ("Hire"). Enabled state is read from
    // Syncfusion's own "e-overlay" class on the .e-toolbar-item (the same signal
    // IsRecordOfferResponseToolbarItemEnabledAsync uses) — never from a Blazor bool-bound aria
    // attribute. Items are matched by their exact text so "Hire"/"Appoint" can't collide with any
    // other item.

    private ILocator ApplicationsToolbarItem(string itemText) =>
        ApplicationsTab.Locator(".e-toolbar-item")
            .Filter(new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(itemText)}\\s*$") });

    private static readonly Regex ToolbarItemDisabledClass = new(@"(^|\s)e-overlay(\s|$)");

    public async Task ExpectToolbarItemEnabledForRowAsync(string candidateNameFragment, string itemText)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButtonAsync(itemText, exact: true, reselectCandidateNameFragment: candidateNameFragment);
        await Assertions.Expect(ApplicationsToolbarItem(itemText))
            .Not.ToHaveClassAsync(ToolbarItemDisabledClass, new() { Timeout = 15_000 });
    }

    public Task ExpectToolbarItemDisabledAsync(string itemText) =>
        Assertions.Expect(ApplicationsToolbarItem(itemText))
            .ToHaveClassAsync(ToolbarItemDisabledClass, new() { Timeout = 15_000 });

    public Task ExpectToolbarTooltipAsync(string tooltipText) =>
        Assertions.Expect(ApplicationsTab.Locator($".e-toolbar [title='{tooltipText}']").First)
            .ToBeAttachedAsync(new() { Timeout = 15_000 });

    public async Task ClickAppointForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await (await ApplicationsToolbarButtonAsync("Appoint", exact: true, reselectCandidateNameFragment: candidateNameFragment)).ClickAsync();
    }

    public async Task ExpectAppointmentPendingHintAsync(Guid applicationId, bool visible)
    {
        var row = ApplicationRowById(applicationId);
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var hint = row.Locator("[data-testid='appointment-pending-hint']");
        if (visible)
            await Assertions.Expect(hint).ToBeVisibleAsync(new() { Timeout = 15_000 });
        else
            await Assertions.Expect(hint).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
