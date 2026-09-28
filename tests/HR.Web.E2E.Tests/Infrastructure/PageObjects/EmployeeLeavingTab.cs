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

    // ── Past Leaving Attempts history section (id="leaving-history-section") ─────

    private ILocator HistorySection => page.Locator("#leaving-history-section");

    /// <summary>
    /// Returns true if the "Past Leaving Attempts" history section is visible — only shown when
    /// there are prior leaving processes and the user can manage employees. The section starts
    /// collapsed by default and must be expanded to view the table.
    /// </summary>
    public Task<bool> IsHistorySectionVisibleAsync() =>
        HistorySection.IsVisibleAsync();

    /// <summary>
    /// Expands the "Past Leaving Attempts" collapsible section if it's not already expanded.
    /// This reveals the history table below the current leaving details.
    /// </summary>
    public async Task ExpandHistorySectionAsync()
    {
        var button = HistorySection.GetByRole(AriaRole.Button);
        var isExpanded = await button.Locator("i.fa-chevron-down").IsVisibleAsync();
        if (!isExpanded)
        {
            await button.ClickAsync();
            // Wait for the table to appear after expansion
            await HistorySection.Locator("table tbody").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
    }

    /// <summary>
    /// Collapses the "Past Leaving Attempts" collapsible section if it's currently expanded.
    /// </summary>
    public async Task CollapseHistorySectionAsync()
    {
        var button = HistorySection.GetByRole(AriaRole.Button);
        var isCollapsed = await button.Locator("i.fa-chevron-right").IsVisibleAsync();
        if (!isCollapsed)
        {
            await button.ClickAsync();
            // Wait for the table to disappear after collapse
            await HistorySection.Locator("table tbody").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        }
    }

    /// <summary>
    /// Returns the number of historical leaving attempts displayed in the history table.
    /// The section must be expanded first (see <see cref="ExpandHistorySectionAsync"/>).
    /// </summary>
    public async Task<int> GetHistoryEntryCountAsync()
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return await rows.CountAsync();
    }

    /// <summary>
    /// Returns a list of status badges from all history entries. The section must be expanded first.
    /// For example: ["Cancelled", "InProgress", "Completed"].
    /// </summary>
    public async Task<IReadOnlyList<string>> GetHistoryStatusesAsync()
    {
        var badges = HistorySection.Locator("table tbody td .badge");
        var count = await badges.CountAsync();
        var statuses = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var text = (await badges.Nth(i).TextContentAsync())?.Trim();
            if (!string.IsNullOrEmpty(text))
                statuses.Add(text);
        }
        return statuses;
    }

    /// <summary>
    /// Returns the Leaving Reason from a specific row in the history table (0-indexed).
    /// The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryLeavingReasonAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(4).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the "Leaving Date" from a specific row in the history table (0-indexed).
    /// The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryLeavingDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(2).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the "Last Working Day" from a specific row in the history table (0-indexed).
    /// The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryLastWorkingDayAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(3).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the "Resignation Received Date" from a specific row in the history table (0-indexed).
    /// The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryResignationDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(1).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the "Replacement Manager" name from a specific row in the history table (0-indexed),
    /// or "—" if none was assigned. The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryReplacementManagerAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(7).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the "Started" (StartedAt) date from a specific row in the history table (0-indexed).
    /// The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryStartedDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(5).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the "Ended" date (CancelledAt or FinalisationCompletedAt) from a specific row
    /// in the history table (0-indexed), or "—" if the process is still in progress.
    /// The section must be expanded first.
    /// </summary>
    public async Task<string?> GetHistoryEndedDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(6).TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the Notes text for a specific history entry if present, or null if the entry has no notes.
    /// The section must be expanded first. Notes are displayed in a separate row with colspan="8".
    /// </summary>
    public async Task<string?> GetHistoryNotesAsync(int rowIndex)
    {
        // Notes are shown in the row immediately after the main row, identified by containing "Notes:"
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        var nextRowLocator = rows.Nth(rowIndex).Locator("xpath=following-sibling::tr[1]");

        if (!await nextRowLocator.IsVisibleAsync())
            return null;

        var text = await nextRowLocator.Locator("td").TextContentAsync();
        if (text?.Contains("Notes:") == true)
        {
            // Extract the text after "Notes: "
            var notesText = text.Replace("Notes:", "").Trim();
            return string.IsNullOrEmpty(notesText) ? null : notesText;
        }

        return null;
    }

    /// <summary>
    /// Returns the Cancellation Reason text for a specific history entry if present, or null
    /// if the entry was not cancelled or has no cancellation reason. The section must be expanded first.
    /// Cancellation reasons are displayed in a separate row with colspan="8".
    /// </summary>
    public async Task<string?> GetHistoryCancellationReasonAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        var nextRowLocator = rows.Nth(rowIndex).Locator("xpath=following-sibling::tr[1]");

        if (!await nextRowLocator.IsVisibleAsync())
            return null;

        var text = await nextRowLocator.Locator("td").TextContentAsync();
        if (text?.Contains("Cancellation Reason:") == true)
        {
            // Extract the text after "Cancellation Reason: "
            var reasonText = text.Replace("Cancellation Reason:", "").Trim();
            return string.IsNullOrEmpty(reasonText) ? null : reasonText;
        }

        return null;
    }
}
