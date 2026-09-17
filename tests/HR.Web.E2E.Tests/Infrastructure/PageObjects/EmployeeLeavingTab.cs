using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the unified "Leaving &amp; Offboarding" workspace on the employee edit page
/// (EmployeeLeavingTab.razor — SPEC-OFF-01). This single tab now covers what used to be two
/// separate tabs: the "Leaving Details" card (id="leaving-details-section") and an embedded
/// offboarding checklist section (id="leaving-checklist-section", rendered by the still-separate
/// <see cref="EmployeeOffboardingTab"/> component). A leaving process is only ever started via the
/// Employee Overview header's "More actions" &gt; "Start offboarding" item (see
/// <see cref="StartLeavingProcessDialog"/>) — the tab itself is hidden entirely from the strip
/// until a process exists (see EmployeeEdit.razor's _showLeavingTab/_showOffboardingTab), though
/// old "?tab=offboarding" bookmarks and the current "?tab=leaving" both resolve to this same tab
/// once it's visible (EmployeeProfileNavigation.ParseTab).
/// </summary>
public sealed class EmployeeLeavingTab(IPage page)
{
    /// <summary>
    /// Opens the "Leaving &amp; Offboarding" tab. Only valid once a leaving process has actually
    /// been started (the tab isn't rendered at all otherwise) — callers should assert
    /// <see cref="IsTabVisibleAsync"/> or use <see cref="HasStartLeavingProcessButtonAsync"/>
    /// beforehand if that isn't already guaranteed.
    /// </summary>
    public async Task OpenAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Leaving");
        await page.WaitForSelectorAsync("#leaving-details-section, .hr-empty-state", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the "Leaving &amp; Offboarding" tab is present in the tab strip. The tab
    /// lives under the "Tasks &amp; Records" group, whose inner strip only renders once that group
    /// is selected — so open the group first, then check.
    /// </summary>
    public async Task<bool> IsTabVisibleAsync()
    {
        await page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Tasks & Records", Exact = true })
            .ClickAsync();
        return await page.Locator(".employee-profile-sections > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Leaving & Offboarding", Exact = true })
            .IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if the "More actions" overflow menu's "Start offboarding" item is present —
    /// only shown while no leaving process is active (see EmployeeEdit.razor's `!_showLeavingTab`
    /// guard / BuildMoreActionsItems). Delegates to EmployeeEditPage.HasStartOffboardingMenuItemAsync.
    /// </summary>
    public Task<bool> HasStartLeavingProcessButtonAsync() =>
        new EmployeeEditPage(page, string.Empty).HasStartOffboardingMenuItemAsync();

    /// <summary>
    /// Returns true if the "No leaving process has been started for this employee." empty state
    /// is visible — EmployeeLeavingTab.razor renders this whenever the leaving-process lookup
    /// returns not-found/null, which can only actually be reached in the UI while the tab is
    /// otherwise visible (e.g. via a stale "offboardingAlreadyStarted" style deep link) since the
    /// tab is hidden entirely from the strip/deep-link fallback for an employee with no process at
    /// all (EmployeeEdit.razor's SectionVisible/ParseTab fallback to Details).
    /// </summary>
    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".hr-empty-state").IsVisibleAsync();

    private ILocator DetailsSection => page.Locator("#leaving-details-section");

    private ILocator DetailsCard => DetailsSection;

    public async Task<string?> GetResignationReceivedDateTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(0).TextContentAsync())?.Trim();

    public async Task<string?> GetLeavingDateTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(1).TextContentAsync())?.Trim();

    public async Task<string?> GetLastWorkingDayTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(2).TextContentAsync())?.Trim();

    public async Task<string?> GetNoticePeriodTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(3).TextContentAsync())?.Trim();

    public async Task<string?> GetNoticeSourceTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(4).TextContentAsync())?.Trim();

    public async Task<string?> GetLeavingReasonTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(5).TextContentAsync())?.Trim();

    /// <summary>Returns the trimmed text of the Notes row, or null if no Notes were recorded (the row is omitted entirely).</summary>
    public async Task<string?> GetNotesTextAsync()
    {
        var dt = DetailsCard.Locator("dl dt").Filter(new() { HasText = "Notes" }).First;
        if (!await dt.IsVisibleAsync())
            return null;
        return (await dt.Locator("xpath=following-sibling::dd[1]").TextContentAsync())?.Trim();
    }

    /// <summary>Returns the trimmed text of the Cancellation Reason row, or null if not shown (only rendered once Status is Cancelled).</summary>
    public async Task<string?> GetCancellationReasonTextAsync()
    {
        var dt = DetailsCard.Locator("dl dt").Filter(new() { HasText = "Cancellation Reason" }).First;
        if (!await dt.IsVisibleAsync())
            return null;
        return (await dt.Locator("xpath=following-sibling::dd[1]").TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the text of the leaving-process status badge ("In Progress"/"Cancelled"/"Completed")
    /// — the second of the three header badges (Employment status, Leaving status, Offboarding status).
    /// </summary>
    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = DetailsSection.Locator(".card-header .badge").Nth(1);
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns the text of the offboarding-status header badge (third badge — "Setup Incomplete"/"In Progress"/"Completed"/…).</summary>
    public async Task<string?> GetOffboardingStatusBadgeTextAsync()
    {
        var badge = DetailsSection.Locator(".card-header .badge").Nth(2);
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns true if the "Amend" button is visible in the Leaving Details card header — only
    /// shown while the leaving process's Status is "InProgress".
    /// </summary>
    public Task<bool> HasAmendButtonAsync() =>
        DetailsSection.GetByRole(AriaRole.Button, new() { Name = "Amend", Exact = true }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the "Cancel Leaving Process" button is visible in the Leaving Details card
    /// header — only shown while the leaving process's Status is "InProgress".
    /// </summary>
    public Task<bool> HasCancelButtonAsync() =>
        DetailsSection.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process", Exact = true }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the persistent "Offboarding has already started for this employee — review
    /// the checklist below for outstanding obligations." banner is visible above the Leaving
    /// Details card. Only rendered when the "offboardingAlreadyStarted=true" query-string flag is
    /// present — set by EmployeeEdit.razor's OnLeavingProcessAmended after a successful Amend whose
    /// response reported AmendLeavingProcessResponse.OffboardingAlreadyStarted.
    /// </summary>
    public Task<bool> HasOffboardingAlreadyStartedWarningAsync() =>
        page.Locator(".alert-warning")
            .Filter(new() { HasText = "Offboarding has already started for this employee" })
            .IsVisibleAsync();

    // ── Embedded checklist section (id="leaving-checklist-section") ──────────────

    /// <summary>The embedded offboarding-checklist sub-section, for scoping checklist-specific locators.</summary>
    public ILocator ChecklistSection => page.Locator("#leaving-checklist-section");

    /// <summary>
    /// Returns the underlying <see cref="EmployeeOffboardingTab"/> page object, scoped to the
    /// checklist section embedded within this unified workspace (rather than a standalone tab).
    /// </summary>
    public EmployeeOffboardingTab Checklist => new(page);
}
