using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy create/edit/view page, including its Applications and
/// Interviews tabs (VacancyApplicationsTab.razor / VacancyInterviewsTab.razor), which render
/// inline on this same page rather than as separate routes.
/// Routes: /companies/{id}/vacancies/new, /vacancies/{id}, /vacancies/{id}/view
/// </summary>
public sealed class VacancyDetailPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/new");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid vacancyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/{vacancyId}");
        await page.WaitForSelectorAsync(".e-tab, span[role='combobox']", new() { Timeout = 20_000 });
    }

    // ── Overview (new/edit form) ─────────────────────────────────────────────────

    /// <summary>
    /// Fills the "Advert Title (optional)" field (bound to Model.AdvertTitle — see
    /// VacancyDetail.razor's RenderDetailsCard). Despite the label rename ("Title" →
    /// "Advert Title (optional)") as part of the "Refactor Duplicate Vacancy Fields" story, the
    /// underlying HrTextBox and its placeholder are unchanged, so this locator still applies. The
    /// field is now genuinely optional — leaving it blank no longer produces a validation error;
    /// see CreateVacancy_WithoutAdvertTitle_UsesPositionProfileTitleAsEffectiveTitle.
    /// </summary>
    public Task FillTitleAsync(string value) =>
        page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(value);

    // NOTE: Vacancy.Location was removed entirely (domain, API, UI) as part of the
    // "Vacancy - Position Profile relationship" epic's location correction — location is now
    // shown only as a read-only value derived from the linked Position Profile. The FillLocationAsync/
    // GetLocationAsync/GetLocationFieldHintAsync methods that used to live here (targeting a
    // "e.g. Remote" placeholder on this page) were removed since that field no longer exists on
    // VacancyDetail.razor; the same placeholder text is still used elsewhere by the unrelated
    // Schedule Interview dialog's free-text Location field (see SelectInterviewerAsync's sibling
    // FillScheduledAtAsync region below), which is not this page's concern.

    public Task FillDescriptionAsync(string value) =>
        page.Locator("textarea.e-input").FillAsync(value);

    /// <summary>
    /// Selects a value from the Hiring Manager dropdown (AllowFiltering enabled). Scoped to
    /// ".col-md-4" — the Recruitment Advert Details card's Location/Hiring Manager fields share
    /// that column width (see VacancyDetail.razor's RenderDetailsCard). The vacancy-level
    /// Department dropdown that used to share this column width was removed as part of the
    /// "Refactor Duplicate Vacancy Fields" story — department is now shown only via the
    /// read-only "Linked Position Profile" card.
    /// </summary>
    public Task SelectHiringManagerAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Hiring Manager" }).First, nameFragment);

    /// <summary>
    /// Selects a value from the Position Profile dropdown (AllowFiltering enabled). Only usable
    /// on the create form / a new vacancy — this field is Enabled="@IsNew" in VacancyDetail.razor,
    /// disabled once a vacancy exists (PositionProfileId cannot be changed via UpdateVacancy).
    /// Scoped to ".col-md-8" — Position Profile now renders in its own card ahead of the Vacancy
    /// Details card (see RenderDetailsCard), in an 8-wide column rather than the 4-wide columns
    /// used by Department/Hiring Manager below it.
    /// </summary>
    public async Task SelectPositionProfileAsync(string titleFragment)
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        await DropDownSelector.SelectAsync(page, group, titleFragment);

        // Confirms Blazor's ValueChanged round-trip to the server actually committed
        // Model.PositionProfileId, not just that the popup closed client-side — without this, a
        // caller that immediately clicks Save can race the round-trip.
        await Assertions.Expect(group.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(titleFragment)), new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Opens the Position Profile dropdown's popup without selecting anything, so its visible
    /// option list can be inspected — e.g. to assert inactive profiles are excluded (the dropdown's
    /// DataSource is active-profiles-only; see VacancyDetail.razor's OnLoadedAsync, which calls
    /// PositionProfileService.ListPositionProfilesAsync with its default includeInactive: false).
    /// </summary>
    public async Task OpenPositionProfileDropdownAsync()
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
    }

    /// <summary>Reads the visible option titles from the (currently open) Position Profile dropdown popup.</summary>
    public async Task<IReadOnlyList<string>> GetPositionProfileDropdownOptionsAsync()
    {
        var items = await page.Locator(".e-popup.e-ddl:visible .e-list-item").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    /// <summary>Reads the current value of the Position Profile dropdown's visible text.</summary>
    public async Task<string?> GetSelectedPositionProfileTextAsync()
    {
        var group = page.Locator(".col-md-8").Filter(new() { HasText = "Position Profile" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    /// <summary>
    /// Returns true if the Position Profile dropdown is disabled — expected once a vacancy has
    /// been created (Enabled="@IsNew" in VacancyDetail.razor; PositionProfileId cannot be changed
    /// after creation).
    /// </summary>
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

    /// <summary>
    /// The "From Position Profile" summary card (data-testid="position-profile-defaults-summary")
    /// that appears once a Position Profile has been selected and its defaults fetched (see
    /// OnPositionProfileChanged in VacancyDetail.razor).
    /// </summary>
    private ILocator PositionProfileDefaultsSummary => page.Locator("[data-testid='position-profile-defaults-summary']");

    public Task<bool> IsPositionProfileDefaultsSummaryVisibleAsync() =>
        PositionProfileDefaultsSummary.IsVisibleAsync();

    /// <summary>Reads the Department value shown in the "From Position Profile" summary card.</summary>
    public async Task<string?> GetSummaryDepartmentNameAsync()
    {
        var dd = PositionProfileDefaultsSummary.Locator("dt:has-text('Department') + dd");
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Reads the Salary Range value shown in the "From Position Profile" summary card, or null if
    /// the selected profile has no salary range set (that row is only rendered when present — see
    /// RenderDetailsCard in VacancyDetail.razor).
    /// </summary>
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
    }

    /// <summary>
    /// The "Correction Reason" required text field (HrTextBox bound to Model.CorrectionReason),
    /// only rendered while the authorised-correction checkbox is checked. Located by its
    /// placeholder, matching this file's existing HrTextBox locator convention (see
    /// FillTitleAsync's doc comment).
    /// </summary>
    private ILocator CorrectionReasonInput => page.GetByPlaceholder("Explain why this correction is required");

    public Task<bool> IsCorrectionReasonFieldVisibleAsync() => CorrectionReasonInput.IsVisibleAsync();

    public Task FillCorrectionReasonAsync(string value) => CorrectionReasonInput.FillAsync(value);

    // ── Linked Position Profile card (existing vacancy only) ─────────────────────
    // "Derive Vacancy Role Information from Position Profile" story: read-only card
    // (data-testid="linked-position-profile-card") rendered whenever an existing vacancy is
    // loaded (edit/view mode, gated on "_vacancy is not null" — never on the "Add Vacancy" create
    // form). Shows the linked Position Profile's own canonical Title/Department/Description, plus
    // an "Inactive" indicator if that profile has since been deactivated. See
    // VacancyDetail.razor's RenderDetailsCard.

    private ILocator LinkedPositionProfileCard => page.Locator("[data-testid='linked-position-profile-card']");

    public Task<bool> IsLinkedPositionProfileCardVisibleAsync() =>
        LinkedPositionProfileCard.IsVisibleAsync();

    /// <summary>
    /// Reads the linked profile's Title, rendered as a plain "span.fw-semibold" (not an input —
    /// this card is entirely read-only). Scoped within the card since the vacancy's own Title
    /// textbox elsewhere on the page is a completely separate element.
    /// </summary>
    public async Task<string?> GetLinkedPositionProfileTitleAsync()
    {
        var span = LinkedPositionProfileCard.Locator("span.fw-semibold");
        return await span.IsVisibleAsync() ? (await span.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Reads the Department value shown in the "Linked Position Profile" card.</summary>
    public async Task<string?> GetLinkedPositionProfileDepartmentAsync()
    {
        var dd = LinkedPositionProfileCard.Locator("dt:has-text('Department') + dd");
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Reads the Description value shown in the "Linked Position Profile" card ("—" if the
    /// profile has no description set — see RenderDetailsCard's ternary fallback).
    /// </summary>
    public async Task<string?> GetLinkedPositionProfileDescriptionAsync()
    {
        var dd = LinkedPositionProfileCard.Locator("dt:has-text('Description') + dd");
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns true if the card shows the "Inactive" indicator (ActiveStatusBadge with
    /// IsActive="false", rendered only when PositionProfileIsActive == false) next to the linked
    /// profile's title. Scoped to the card so it can't collide with any other "Inactive" badge
    /// elsewhere on the page (e.g. the vacancy's own StatusBadge never renders that text).
    /// </summary>
    public Task<bool> IsLinkedPositionProfileInactiveBadgeVisibleAsync() =>
        LinkedPositionProfileCard.GetByText("Inactive", new() { Exact = true }).IsVisibleAsync();

    /// <summary>
    /// The fallback message shown instead of profile details when a (legacy) vacancy has no
    /// linked Position Profile at all (_vacancy.PositionProfileTitle is null).
    /// </summary>
    public async Task<string?> GetLinkedPositionProfileEmptyMessageAsync()
    {
        var p = LinkedPositionProfileCard.Locator("p.text-muted");
        return await p.IsVisibleAsync() ? (await p.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// The "View Position Profile" link in the "Linked Position Profile" card header — rendered
    /// only when the vacancy has a linked Position Profile (_vacancy.PositionProfileId is not
    /// null). Navigates to /companies/{CompanyId}/position-profiles/{PositionProfileId}/view.
    /// </summary>
    public Task<bool> IsViewPositionProfileLinkVisibleAsync() =>
        LinkedPositionProfileCard.GetByRole(AriaRole.Link, new() { Name = "View Position Profile" }).IsVisibleAsync();

    public Task ClickViewPositionProfileLinkAsync() =>
        LinkedPositionProfileCard.GetByRole(AriaRole.Link, new() { Name = "View Position Profile" }).ClickAsync();

    /// <summary>
    /// True if the (renamed) "Recruitment Advert Details" card header is present — this card was
    /// previously headed "Vacancy Details" before the "Derive Vacancy Role Information from
    /// Position Profile" story; the fields underneath were later further reduced (the vacancy-level
    /// Department dropdown was removed entirely, and Title/Description were renamed to "Advert
    /// Title (optional)"/"Advert Description (optional)") by the "Refactor Duplicate Vacancy
    /// Fields" story.
    /// </summary>
    public Task<bool> HasRecruitmentAdvertDetailsHeaderAsync() =>
        page.Locator(".card-header h5").Filter(new() { HasText = "Recruitment Advert Details" }).IsVisibleAsync();

    /// <summary>
    /// The "Recruitment Advert Details" card container itself (not just its header) — used to
    /// scope assertions about which fields render inside it, e.g. confirming the vacancy-level
    /// Department dropdown removed by the "Refactor Duplicate Vacancy Fields" story is genuinely
    /// gone from this specific card, without accidentally matching the separate, unrelated
    /// "Linked Position Profile" card's read-only Department &lt;dt&gt;/&lt;dd&gt; pair.
    /// </summary>
    private ILocator RecruitmentAdvertDetailsCard =>
        page.Locator(".card").Filter(new() { Has = page.Locator(".card-header h5:has-text('Recruitment Advert Details')") });

    /// <summary>
    /// Counts any "Department" field label within the Recruitment Advert Details card. Expected to
    /// be zero — the vacancy-level Department dropdown was removed entirely as part of the
    /// "Refactor Duplicate Vacancy Fields" story; department is now shown only via the separate,
    /// read-only "Linked Position Profile" card.
    /// </summary>
    public Task<int> CountDepartmentFieldsInAdvertDetailsCardAsync() =>
        RecruitmentAdvertDetailsCard.Locator("label.form-label", new() { HasText = "Department" }).CountAsync();

    /// <summary>
    /// Reads the "Advert Title" field label's exact text, if present. No longer suffixed with
    /// "(optional)" — the field itself is still genuinely optional, but that qualifier was
    /// dropped from every field label across this card (see <see cref="HasOptionalSuffixAsync"/>).
    /// </summary>
    public Task<bool> HasAdvertTitleLabelAsync() =>
        RecruitmentAdvertDetailsCard.GetByText("Advert Title", new() { Exact = true }).IsVisibleAsync();

    /// <summary>Reads the "Advert Description" field label's exact text, if present (no "(optional)" suffix — see <see cref="HasAdvertTitleLabelAsync"/>).</summary>
    public Task<bool> HasAdvertDescriptionLabelAsync() =>
        RecruitmentAdvertDetailsCard.GetByText("Advert Description", new() { Exact = true }).IsVisibleAsync();

    /// <summary>
    /// True if any field label within the Recruitment Advert Details card still contains the
    /// literal text "(optional)" — expected false; that qualifier was removed from every label on
    /// this card.
    /// </summary>
    public async Task<bool> HasOptionalSuffixAsync() =>
        await RecruitmentAdvertDetailsCard.GetByText("(optional)", new() { Exact = false }).CountAsync() > 0;

    /// <summary>
    /// Reads the page's main heading text — for an existing vacancy this is
    /// "_vacancy.EffectiveTitle" (the vacancy's own AdvertTitle if set, otherwise the linked
    /// Position Profile's title), not the raw AdvertTitle field, as of the "Refactor Duplicate
    /// Vacancy Fields" story (see VacancyDetail.razor's non-IsNew branch).
    /// </summary>
    public async Task<string?> GetHeaderTextAsync()
    {
        var h1 = page.Locator("h1").First;
        return (await h1.TextContentAsync())?.Trim();
    }

    /// <summary>Reads the vacancy's status text from the StatusBadge next to the page heading (e.g. "Draft", "Open").</summary>
    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = page.Locator(".status-badge").First;
        return (await badge.TextContentAsync())?.Trim();
    }

    /// <summary>Reads the current value of the Hiring Manager dropdown's visible text.</summary>
    public async Task<string?> GetSelectedHiringManagerTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Hiring Manager" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task SaveNewVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Saves changes to an already-existing vacancy (edit mode's "Overview" tab Save button,
    /// distinct from the applications/interviews tabs' own dialog Save buttons). Functionally
    /// identical to <see cref="SaveNewVacancyAsync"/> — EditPageBase.OnSavedAsync navigates to
    /// ListUrl ("/companies/{id}/vacancies") on any successful save, new or existing — but named
    /// separately here for call-site clarity when editing an existing record (e.g. changing its
    /// Position Profile).
    /// </summary>
    public Task SaveExistingVacancyAsync() => SaveNewVacancyAsync();

    /// <summary>
    /// Clicks the Overview tab's "Save" button without waiting for navigation — for tests that
    /// expect client- or server-side validation to keep the form on the page (e.g. an authorised
    /// correction submitted with an empty Correction Reason; see UpdateVacancyValidator). Compare
    /// <see cref="SaveExistingVacancyAsync"/>, which waits for the post-save redirect to the list.
    /// </summary>
    public Task ClickSaveButtonAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    /// <summary>
    /// Reads the current value of the (now-optional) "Advert Title (optional)" field itself —
    /// i.e. the raw AdvertTitle, not the resolved "EffectiveTitle" shown in the page header/list;
    /// see <see cref="GetHeaderTextAsync"/> for that.
    /// </summary>
    public Task<string> GetTitleAsync() =>
        page.GetByPlaceholder("e.g. Senior Software Engineer").InputValueAsync();

    // ── Close / unsaved-changes prompt (EditPageBase) ────────────────────────────
    // Same shared UnsavedChangesDialog.razor component used by every EditPageBase-derived
    // page (see DepartmentEditPage.cs for the representative test coverage of this behavior).

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    // ── Publish Vacancy (Draft/OnHold → Open) ────────────────────────────────────
    // "Publish Vacancy" button, shown next to the Close Vacancy button whenever the loaded
    // vacancy's status is Draft or OnHold (VacancyDetail.razor's CanPublish); calls
    // VacancyService.PublishVacancyAsync (POST .../vacancies/{id}/publish) and reloads the
    // vacancy on success, same pattern as CloseVacancyAsync.

    public Task<bool> IsPublishButtonVisibleAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Publish Vacancy" }).IsVisibleAsync();

    public async Task PublishVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish Vacancy" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Publish Vacancy" }))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True if a tab with the exact given name (e.g. "Applications"/"Interviews"/"Kanban") is
    /// present — these are hidden entirely for a Draft-status vacancy (VacancyDetail.razor's
    /// "_vacancy is not null &amp;&amp; _vacancy.Status != "Draft"" gate), not just disabled.
    /// </summary>
    public Task<bool> HasTabAsync(string name) =>
        page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true }).IsVisibleAsync();

    public async Task OpenApplicationsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Applications" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='vacancy-applications-tab']", new() { Timeout = 15_000 });
    }

    public async Task OpenInterviewsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Interviews" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='vacancy-interviews-tab']", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Opens the "Kanban" tab (embeds VacancyKanbanBoard.razor inline — see "Recruitment Kanban"
    /// tickets #69-#73). Waits for the board's own data-testid, not just the tab becoming active,
    /// since the board does its own async load once mounted.
    /// </summary>
    public async Task OpenKanbanTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Kanban" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='vacancy-kanban-board']", new() { Timeout = 15_000 });
    }

    // ── Applications tab: Add Candidate ──────────────────────────────────────────

    public async Task ClickAddCandidateAsync()
    {
        await page.Locator("[data-testid='add-application-btn']").ClickAsync();
        await page.Locator("[role='dialog'].add-application-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task SelectCandidateInAddDialogAsync(string nameOrEmailFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-application-dialog"), nameOrEmailFragment);

    /// <summary>
    /// Selects a value from the "Source (optional)" dropdown in the (currently open) Add Candidate
    /// dialog (VacancyApplicationsTab.razor's _sourceOptions — "Unspecified", "Direct", "Referral",
    /// "External Recruiter", "Job Board", "Careers Site"). This is the dialog's second combobox
    /// (index 1) — the Candidate picker is the first (index 0).
    /// </summary>
    public Task SelectAddApplicationSourceAsync(string sourceLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-application-dialog"), sourceLabel, index: 1);

    /// <summary>
    /// Selects a value from the "Recruiter" dropdown that only renders in the Add Candidate dialog
    /// once Source="ExternalRecruiter" is picked. This is the dialog's third combobox (index 2).
    /// </summary>
    public Task SelectAddApplicationRecruiterAsync(string agencyNameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-application-dialog"), agencyNameFragment, index: 2);

    /// <summary>
    /// The non-blocking "This recruiter isn't currently assigned to this vacancy." warning
    /// (data-testid="recruiter-not-assigned-warning") shown under the Recruiter dropdown when the
    /// chosen recruiter isn't among this vacancy's currently-assigned active recruiters.
    /// </summary>
    public Task<bool> IsRecruiterNotAssignedWarningVisibleAsync() =>
        page.Locator("[data-testid='recruiter-not-assigned-warning']").IsVisibleAsync();

    public async Task SubmitAddApplicationAsync()
    {
        await page.Locator(".add-application-dialog .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog'].add-application-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the Add Candidate dialog's "Add" button without waiting for the dialog to close — for
    /// tests expecting client-side validation (e.g. "Please select a recruiter…") to keep it open.
    /// </summary>
    public Task ClickAddApplicationSubmitButtonAsync() =>
        page.Locator(".add-application-dialog .e-footer-content button:has-text('Add')").ClickAsync();

    /// <summary>
    /// Reads the Applications grid's "Source" column text for the row matching
    /// <paramref name="candidateNameFragment"/> — populated lazily per-application (see
    /// VacancyApplicationsTab.razor's _sourceDetails/LoadAsync), so this may briefly show "—" while
    /// that N+1 detail fetch is still in flight; callers should generally reload/re-check after the
    /// add dialog has closed and LoadAsync has re-run.
    /// </summary>
    public async Task<string?> GetApplicationSourceColumnTextAsync(string candidateNameFragment)
    {
        var cell = ApplicationRow(candidateNameFragment).First.Locator(".e-rowcell").Last;
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

    /// <summary>Returns the full trimmed text of the application row matching <paramref name="candidateNameFragment"/>, or null if not found.</summary>
    public async Task<string?> GetApplicationRowTextAsync(string candidateNameFragment)
    {
        var row = ApplicationRow(candidateNameFragment).First;
        return await row.IsVisibleAsync() ? (await row.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetApplicationStatusAsync(string candidateNameFragment)
    {
        var badge = ApplicationRow(candidateNameFragment).First.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Selects the application row matching <paramref name="candidateNameFragment"/> (click
    /// anywhere in the row — Syncfusion Grid's RowSelected fires from a plain row click, no
    /// checkbox/radio needed here) and waits briefly for the toolbar's enabled state to catch up
    /// (VacancyApplicationsTab.razor's RefreshToolbarStateAsync round-trips to re-enable the
    /// relevant buttons after selection).
    /// </summary>
    private async Task SelectApplicationRowAsync(string candidateNameFragment)
    {
        await ApplicationRow(candidateNameFragment).First.Locator(".e-rowcell").First.ClickAsync();
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>
    /// Returns the (enabled/disabled) toolbar button with the given accessible name within the
    /// Applications tab's own grid toolbar — scoped there so this can't collide with any other
    /// toolbar/button of the same name elsewhere on the page.
    /// </summary>
    private ILocator ApplicationsToolbarButton(string name) =>
        ApplicationsTab.Locator(".e-toolbar").GetByRole(AriaRole.Button, new() { Name = name });

    /// <summary>
    /// True if no per-row action buttons (the older Actions-column pattern) render inside any
    /// application row — the toolbar (see <see cref="ApplicationsToolbarButton"/>) is now the only
    /// place these actions live.
    /// </summary>
    public async Task<bool> HasAnyPerRowApplicationActionButtonAsync()
    {
        var rows = ApplicationsTab.Locator(".e-grid .e-row");
        var count = await rows.Locator("button").CountAsync();
        return count > 0;
    }

    public async Task ClickScheduleInterviewForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButton("Schedule Interview").ClickAsync();
    }

    public async Task ClickOfferForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButton("Offer").ClickAsync();
    }

    public async Task ClickRejectForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButton("Reject").ClickAsync();
    }

    public async Task ClickWithdrawForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButton("Withdraw").ClickAsync();
    }

    public async Task ClickHireForAsync(string candidateNameFragment)
    {
        await SelectApplicationRowAsync(candidateNameFragment);
        await ApplicationsToolbarButton("Hire").ClickAsync();
    }

    // ── Schedule Interview dialog ────────────────────────────────────────────────

    public async Task WaitForScheduleDialogAsync() =>
        await page.Locator("[role='dialog'].schedule-interview-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    /// <summary>
    /// Selects an interviewer from the Schedule Interview dialog's Interviewer dropdown.
    /// DropDownSelector itself confirms Blazor's ValueChanged round-trip actually committed the
    /// selection before returning — see its own doc comment.
    /// </summary>
    public Task SelectInterviewerAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".schedule-interview-dialog"), nameFragment);

    /// <param name="ddMMyyyyHHmm">
    /// Must match the "Scheduled At" SfDateTimePicker's explicit Format="dd/MM/yyyy HH:mm" in
    /// VacancyApplicationsTab.razor — 24-hour, no AM/PM suffix (e.g. "01/09/2026 10:00"). A string
    /// the picker can't parse against that format silently leaves the bound value null rather than
    /// erroring, which then fails ConfirmScheduleAsync's "select an interviewer and a scheduled
    /// time" check and leaves the dialog open.
    /// </param>
    public async Task FillScheduledAtAsync(string ddMMyyyyHHmm)
    {
        // ".schedule-interview-dialog input.e-input" alone also matches the Interviewer
        // dropdown's own readonly input, which comes first in DOM order — its wrapping
        // span[role='combobox'] intercepts pointer events on that input, so a bare .First there
        // can never actually be clicked. Scope to the "Scheduled At" field's own container.
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
    }

    // ── Reject dialog ────────────────────────────────────────────────────────────

    public async Task SubmitRejectAsync()
    {
        await page.Locator(".reject-candidate-dialog .e-footer-content button:has-text('Reject')").ClickAsync();
        await page.Locator("[role='dialog'].reject-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Hire dialog ───────────────────────────────────────────────────────────────

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

    /// <summary>Fills the Employee Number field in the (currently open) Hire Candidate dialog.</summary>
    public Task FillHireEmployeeNumberAsync(string value) =>
        page.Locator(".hire-candidate-dialog").GetByPlaceholder("e.g. EMP-001").FillAsync(value);

    /// <summary>
    /// Selects a value from a Syncfusion SfDropDownList in the (currently open) Hire Candidate
    /// dialog, identified by nearby label text — used for the Employment Type and Manager
    /// dropdowns (Nationality and Gender have their own dedicated methods above, predating this
    /// generic helper). As of the "Vacancy - Position Profile relationship" epic, the Department,
    /// Location and Position Profile dropdowns this helper used to also target were removed from
    /// the dialog entirely — those values are now derived server-side from the Vacancy's own
    /// linked Position Profile and shown read-only; see
    /// <see cref="GetHireDerivedPositionProfileTextAsync"/> and
    /// <see cref="GetHireDerivedLocationTextAsync"/>.
    /// </summary>
    public Task SelectHireDropdownAsync(string labelText, string optionText) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator(".hire-candidate-dialog .col-md-6").Filter(new() { HasText = labelText }).First,
            optionText);

    /// <summary>Reads the current value of a Hire Candidate dialog dropdown's visible text, identified by nearby label text.</summary>
    public async Task<string?> GetSelectedHireDropdownTextAsync(string labelText)
    {
        var group = page.Locator(".hire-candidate-dialog .col-md-6")
            .Filter(new() { HasText = labelText })
            .First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    /// <summary>
    /// Reads the read-only "Position Profile" value shown in the (currently open) Hire Candidate
    /// dialog (data-testid="hire-derived-position-profile") — derived server-side from the
    /// Vacancy's own linked Position Profile (VacancyApplicationsTab.razor's OpenHireDialog sets
    /// this from GetVacancyResponse.PositionProfileTitle, falling back to EffectiveTitle). No
    /// longer a selectable dropdown as of the "Vacancy - Position Profile relationship" epic.
    /// </summary>
    public async Task<string?> GetHireDerivedPositionProfileTextAsync() =>
        (await page.Locator("[data-testid='hire-derived-position-profile']").TextContentAsync())?.Trim();

    /// <summary>
    /// Reads the read-only "Location" value shown in the (currently open) Hire Candidate dialog
    /// (data-testid="hire-derived-location") — derived server-side from the Vacancy's
    /// EffectiveLocation (own Location override, or its linked Position Profile's location). No
    /// longer a selectable dropdown as of the "Vacancy - Position Profile relationship" epic.
    /// </summary>
    public async Task<string?> GetHireDerivedLocationTextAsync() =>
        (await page.Locator("[data-testid='hire-derived-location']").TextContentAsync())?.Trim();

    /// <summary>
    /// Returns true if the (currently open) Hire Candidate dialog contains a selectable dropdown
    /// (span[role='combobox']) labelled with the given text — used to assert the removed manual
    /// Department/Location/Position Profile dropdowns are genuinely gone, not just relabelled.
    /// </summary>
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

    /// <summary>
    /// Clicks the Hire dialog's "Hire" button without waiting for the dialog to close — for tests
    /// that expect client-side validation to keep the dialog open (see <see cref="SubmitHireAsync"/>
    /// for the happy-path variant that waits for the dialog to hide after a successful hire).
    /// </summary>
    public Task ClickHireSubmitButtonAsync() =>
        page.Locator(".hire-candidate-dialog .e-footer-content button:has-text('Hire')").ClickAsync();

    /// <summary>Clicks "Cancel" on the (currently open) Hire Candidate dialog and waits for it to close.</summary>
    public async Task CancelHireDialogAsync()
    {
        await page.Locator(".hire-candidate-dialog .e-footer-content button:has-text('Cancel')").ClickAsync();
        await page.Locator("[role='dialog'].hire-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<bool> HasDialogErrorAsync(string dialogCssClass) =>
        await page.Locator($".{dialogCssClass} .alert-danger").IsVisibleAsync();

    // ── Overview tab: Recruitment Agency dropdown (ticket #81/#94) ───────────────
    // Optional SfDropDownList bound to Model.AssignedRecruiterId (FK to ExternalRecruiter, not the
    // removed VacancyRecruiterAssignment/Recruiters-tab feature above). DataSource is active-only
    // (VacancyDetail.razor's OnLoadedAsync calls ListExternalRecruitersAsync(isActive: true)), with
    // a prepended "Not assigned" sentinel item (Guid.Empty) rather than ShowClearButton, per this
    // codebase's convention for optional dropdowns. Scoped to ".col-md-4" like Hiring Manager above,
    // since both fields share that column width in the "Recruitment Advert Details" card.
    private ILocator RecruitmentAgencyField =>
        page.Locator(".col-md-4").Filter(new() { HasText = "Recruitment Agency" }).First;

    public Task SelectRecruitmentAgencyAsync(string agencyNameFragment) =>
        DropDownSelector.SelectAsync(page, RecruitmentAgencyField, agencyNameFragment);

    /// <summary>Reads the current value of the Recruitment Agency dropdown's visible text.</summary>
    public async Task<string?> GetSelectedRecruitmentAgencyTextAsync()
    {
        var input = RecruitmentAgencyField.Locator(".e-input-group input").First;
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Opens the Recruitment Agency dropdown's popup without selecting anything, so its visible
    /// option list can be inspected — e.g. to assert an inactive agency is excluded (the
    /// DataSource is active-recruiters-only).
    /// </summary>
    public async Task OpenRecruitmentAgencyDropdownAsync()
    {
        await RecruitmentAgencyField.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
    }

    /// <summary>Reads the visible option names from the (currently open) Recruitment Agency dropdown popup.</summary>
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

    // ── Interviews tab ────────────────────────────────────────────────────────────

    private ILocator InterviewRow(string candidateNameFragment) =>
        page.Locator("[data-testid='vacancy-interviews-tab'] .e-grid .e-row").Filter(new() { HasText = candidateNameFragment });

    public async Task<string?> GetInterviewOutcomeAsync(string candidateNameFragment)
    {
        var badge = InterviewRow(candidateNameFragment).First.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
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

}
