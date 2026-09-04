using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee list page (/companies/{companyId}/employees).
/// </summary>
public sealed class EmployeeListPage(IPage page, string baseUrl)
{
    /// <summary>
    /// Builds a word-order-agnostic matcher for a "First Last" name fragment. EmployeeList.razor's
    /// grid renders Last Name before First Name (see the GridColumns order), so a row/cell's text
    /// reads "Bennett Laura ..." — a plain HasText substring match on "Laura Bennett" (First-Last
    /// order) never matches. This requires every whitespace-separated word in
    /// <paramref name="nameFragment"/> to appear somewhere in the target's text, in any order —
    /// works whether given a single word (e.g. just a last name) or a full "First Last" name.
    /// </summary>
    private static Regex NameMatcher(string nameFragment)
    {
        var words = nameFragment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lookaheads = string.Concat(words.Select(w => $"(?=.*{Regex.Escape(w)})"));
        return new Regex(lookaheads, RegexOptions.Singleline);
    }

    // Waiting for ".e-grid" alone is NOT sufficient to guarantee rows are queryable: Syncfusion's
    // EJ2 grid does its own JS render pass to populate ".e-row"/".e-rowcell" into the DOM on a
    // separate tick after the Blazor component itself has mounted. Waiting for the row selector
    // (or its empty-state sibling) directly is the only wait that's actually tied to data being
    // present — see the same pattern in VacancyListPage etc.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees");
        // Previously this tried to confirm the circuit had connected by watching for a
        // spinner→grid transition via a MutationObserver installed with page.EvaluateAsync
        // *after* navigation. That's a race: if Blazor's prerender→interactive spinner cycle
        // finishes before the observer script gets installed (routine on a fast/local run),
        // the transition is never observed, window._listReady never flips true, and the wait
        // times out — which made most tests starting with GoToAsync fail. RowsRenderedSelector
        // alone is sufficient and race-free: Syncfusion can only populate real ".e-row"/
        // ".e-rowcell" data via its JS interop once the interactive circuit is connected and
        // the component's data fetch has completed, so waiting for it already proves both.
        // Same pattern as VacancyListPage/PublicHolidayListPage etc.
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewEmployeeAsync()
    {
        // Renamed from the generic "Add" to "Add employee" so the primary toolbar action reads
        // unambiguously (see SearchPageBase.AddButtonText / EmployeeList.AddButtonText override).
        //
        // The button's own Disabled state (SearchPageBase's IsAddDisabled) is bound to
        // Session.IsReadOnly, NOT to whether the click actually does anything — EmployeeList's
        // GetAddUrl() separately gates on "_canCreateEmployee", set by its own independent async
        // permission check (UserService.HasPermissionAsync) in OnInitializedAsync. GoToAsync above
        // only waits for the grid's own rows to render, an unrelated async path — if the
        // permission check hasn't resolved by the time this click fires, SearchPageBase's "hr-add"
        // handler silently no-ops (GetAddUrl() returns null, nothing navigates, no error). Confirmed
        // via a fully isolated single-test run still failing deterministically — not a load/timing
        // flake, a genuine race between two independent OnInitializedAsync tasks that this page
        // object's wait condition doesn't cover. Retry the click rather than trusting one attempt.
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Add employee" });
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await button.ClickAsync();
            try
            {
                await page.WaitForURLAsync("**/employees/new**", new() { Timeout = attempt < 5 ? 2_000 : 10_000 });
                return;
            }
            catch (TimeoutException) when (attempt < 5)
            {
                // Permission check likely still pending when we clicked — try again.
            }
        }
    }

    /// <summary>
    /// Returns true if an employee matching <paramref name="nameFragment"/> exists, searching for
    /// it via the page's own search box rather than scanning whatever's on the current unfiltered
    /// page. EmployeeList.razor loads an unfiltered page capped at 100 rows sorted by last name —
    /// on this shared, long-lived E2E database that cap is easy to exceed, so a specific employee
    /// (e.g. one a test just created) can silently fall outside it with no indication why. The
    /// search box round-trips to the server (SearchPageBase.OnSearchChanged), so it finds the
    /// employee regardless of how many others sort before them.
    /// </summary>
    public async Task<bool> HasEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var searchInput = page.GetByPlaceholder("Search by name, email or employee number");
        await searchInput.FillAsync(nameFragment);
        // HrTextBox (SfTextBox) only raises ValueChanged on blur/change, not on the "input" event
        // Playwright's FillAsync dispatches — without an explicit Enter/blur here,
        // SearchPageBase.OnSearchChanged never actually fires and the grid silently keeps showing
        // the unfiltered rows.
        await searchInput.PressAsync("Enter");
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        // The search box already narrowed the grid server-side to rows matching nameFragment
        // (name/email/employee number) — a single ".e-rowcell" can never contain a full "First
        // Last" name anyway (Last Name and First Name are separate columns; see NameMatcher's
        // doc comment), so just confirm at least one real data row came back rather than the
        // empty-state row.
        return await page.Locator(".e-grid .e-row").CountAsync() > 0;
    }

    public async Task<IReadOnlyList<string>> GetEmployeeNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Checks the row-selection checkbox (GridColumn Type="CheckBox") for the employee whose row
    /// contains <paramref name="nameFragment"/>. Clicking the Syncfusion checkbox's own wrapper
    /// span (rather than the underlying hidden native input) mirrors a real user click and is the
    /// documented way to toggle a grid checkbox column.
    /// </summary>
    public async Task CheckEmployeeRowAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-grid .e-row").Filter(new() { HasTextRegex = NameMatcher(nameFragment) }).First;
        var checkbox = row.Locator(".e-checkbox-wrapper").First;
        var input = checkbox.Locator("input[type='checkbox']").First;
        var wasChecked = await input.IsCheckedAsync();

        await checkbox.ClickAsync();

        // The click itself only updates Syncfusion's client-side checkbox state immediately —
        // SelectedCount/_hasSelection and the "Update selected (N)" button text are updated by
        // SearchPageBase.OnRowSelected/OnRowDeselected on the SERVER, over a Blazor Server
        // round-trip that isn't guaranteed to have completed by the time ClickAsync returns. A
        // caller checking a second row immediately after (e.g.
        // MultiRowSelection_ShowsCorrectCountOnUpdateSelectedButton) can otherwise read a stale
        // count from before this row's round-trip lands. Wait for the checkbox to actually flip
        // (this method also backs UncheckEmployeeRowAsync, so the target state can be either way)
        // as a proxy for that round-trip having landed.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await input.IsCheckedAsync() == wasChecked && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    /// <summary>
    /// Returns true if the "Bulk Update" dropdown's own "Selected Employees" menu item is
    /// currently disabled (BulkUpdateMenu's HasSelection parameter, wired from EmployeeList's
    /// _hasSelection) — not the dropdown button itself, which always stays enabled since its other
    /// two items ("Import", "Download Template") don't require any row selection. Opens the
    /// dropdown to inspect the item, then closes it again (Escape) so the grid is left as this
    /// method found it.
    /// </summary>
    public async Task<bool> IsBulkUpdateButtonDisabledAsync()
    {
        // Renamed from "Bulk Update" to "Update selected" (optionally suffixed with the selected
        // count, e.g. "Update selected (2)" — see BulkUpdateMenu.ButtonText). Playwright's Name
        // matching is substring by default, so this still matches with or without the count.
        await page.GetByRole(AriaRole.Button, new() { Name = "Update selected" }).ClickAsync();
        var item = page.GetByRole(AriaRole.Menuitem, new() { Name = "Selected Employees" });
        await item.WaitForAsync(new() { Timeout = 10_000 });

        var ariaDisabled = await item.GetAttributeAsync("aria-disabled");
        var hasDisabledClass = (await item.GetAttributeAsync("class"))?.Contains("e-disabled") ?? false;

        await page.Keyboard.PressAsync("Escape");

        return ariaDisabled == "true" || hasDisabledClass;
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown (BulkUpdateMenu) and clicks its "Selected
    /// Employees" item, reaching BulkCompensationUpdateDialog for whichever row(s) are currently
    /// checked — the same destination the old plain "Bulk Update" button used to open directly.
    /// Waits for the SfDialog (identified by its own CssClass, scoped to the role="dialog" element
    /// to avoid matching any other node that shares the class — see the Playwright locator
    /// conventions note re: Syncfusion CssClass reuse) to become visible.
    /// </summary>
    public async Task ClickBulkUpdateAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Update selected" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Selected Employees" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-update-dialog",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// The page's own success banner (_actionSuccess), shown after a bulk update dialog applies
    /// successfully and closes (see EmployeeList.HandleBulkUpdateApplied).
    /// </summary>
    public async Task<string?> GetActionSuccessMessageAsync()
    {
        var banner = page.Locator(".alert-success");
        return await banner.WaitUntilVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// The page's own error banner (_actionError), e.g. shown if downloading the compensation
    /// import template fails.
    /// </summary>
    public async Task<string?> GetActionErrorMessageAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.WaitUntilVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown (BulkUpdateMenu) and clicks "Download Template",
    /// triggering a browser download of the compensation import template — mirrors
    /// BulkCompensationUpdatePage.ClickDownloadTemplateAsync for the full-page equivalent.
    /// </summary>
    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await OpenBulkUpdateMenuItemAsync("hr-bulk-download-template");
        var download = await downloadTask;
        return download.SuggestedFilename;
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown and clicks "Import", reaching
    /// BulkCompensationImportDialog (identified by its own CssClass,
    /// "bulk-compensation-import-dialog") for the Import from Excel flow.
    /// </summary>
    public async Task ClickBulkImportAsync()
    {
        await OpenBulkUpdateMenuItemAsync("hr-bulk-import");
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-import-dialog",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the "Bulk Update" toolbar button (BulkUpdateMenu.razor's SfDropDownButton) and then
    /// the named menu item within the popup it opens, targeted by <paramref name="itemId"/> — the
    /// item's own DropDownMenuItem.Id (e.g. "hr-bulk-import", "hr-bulk-download-template",
    /// "hr-bulk-selected" — see BulkUpdateMenu.razor's BuildItems), which Syncfusion renders as the
    /// real "id" HTML attribute on the item's own &lt;li&gt;. Previously targeted by accessible
    /// role+name instead, scoped to the ".e-dropdown-popup" container that was just confirmed
    /// open — but EmployeeList.razor's toolbar also mounts a second SfDropDownButton (ExportMenu),
    /// each with its own ".e-dropdown-popup", and that scoping still couldn't reliably prove which
    /// popup instance was actually open when more than one such container exists in the DOM
    /// (Syncfusion popups are commonly pre-rendered closed, not lazily created on first open) —
    /// observed as "Import" never becoming visible even though its popup and item genuinely exist.
    /// An id is unique across the whole document by definition, so targeting it directly sidesteps
    /// the ambiguity entirely rather than needing to first prove which popup is which.
    ///
    /// A single click on a just-mounted SfDropDownButton can land before Syncfusion's JS interop
    /// has attached its click listener — the click is silently swallowed, no popup ever opens, and
    /// the follow-up item click then waits the full default timeout for an item that will never
    /// appear (same class of race DropDownSelector.SelectAsync guards against for SfDropDownList
    /// combo boxes). Retries the button click a few times, but only when the popup itself never
    /// opened at all — checked via its own ".e-dropdown-popup" container rather than the specific
    /// item, since re-clicking the trigger while the popup IS already open toggles a
    /// SfDropDownButton closed again (unlike SfDropDownList's combobox, which stays open on a
    /// same-target re-click) — retrying past that point would just flap the menu open/closed and
    /// never let a genuinely-slow-to-render item catch up.
    /// </summary>
    private async Task OpenBulkUpdateMenuItemAsync(string itemId)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Update selected" });
        var popup = page.Locator(".e-dropdown-popup");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (await popup.IsVisibleAsync())
                break;

            await button.ClickAsync();
            try
            {
                await popup.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < 3 ? 2_000 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                // Popup never opened — listener likely wasn't bound yet. Try again.
            }
        }

        var menuItem = page.Locator($"#{itemId}");
        await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await menuItem.ClickAsync();
    }

    public async Task ClickEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        // The unfiltered page is capped at 100 rows sorted by last name (same reasoning as
        // HasEmployeeAsync above) — a just-created employee can easily fall outside that cap on
        // this shared, long-lived E2E database, leaving the row locator below waiting forever.
        // Search first so the target row is guaranteed to be on the (now filtered) page.
        await SearchAsync(nameFragment);

        // The combined "Employee" column (avatar + full name + employee number, see
        // EmployeeList.razor's Employee GridColumn Template) renders as a single <a> per row.
        // Find the row first (order-agnostic across its rendered text — see NameMatcher), then
        // click that row's link rather than filtering the link itself by the full nameFragment.
        var link = page.Locator(".e-grid .e-row")
            .Filter(new() { HasTextRegex = NameMatcher(nameFragment) })
            .First
            .Locator(".e-rowcell a")
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
        // The edit page shows a spinner while its LoadAsync() runs; without this wait, callers
        // that immediately assert on page content (e.g. tab visibility) can race the load and
        // observe the page still in its loading state.
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 15_000 });
    }

    public async Task SearchAsync(string query)
    {
        // EmployeeList.razor's search placeholder is "Search by name, email or employee number"
        // (see EmployeeList.razor) — matches HasEmployeeAsync's placeholder text above.
        var searchInput = page.GetByPlaceholder("Search by name, email or employee number");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        // HrTextBox (SfTextBox) only raises ValueChanged on blur/change, not on the "input" event
        // Playwright's FillAsync dispatches — without an explicit Enter/blur here,
        // SearchPageBase.OnSearchChanged never actually fires and the grid silently keeps showing
        // the unfiltered rows (same reasoning as HasEmployeeAsync above).
        await searchInput.PressAsync("Enter");
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    // ── User Account column (tickets #90/#91 — "User Account" status + Quick Invite) ──────────

    /// <summary>
    /// Returns the row matching <paramref name="nameFragment"/> across the separate Last Name/
    /// First Name cells (see NameMatcher) — used as the anchor for all of the User-Account-column
    /// helpers below.
    /// </summary>
    private ILocator Row(string nameFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasTextRegex = NameMatcher(nameFragment) }).First;

    /// <summary>
    /// The "User Account" column cell (its <c>.user-account-cell</c> wrapper) for the row matching
    /// <paramref name="nameFragment"/>. Targeted by its own marker class rather than by cell index
    /// (<c>.e-rowcell:last</c>) — the grid also carries a hidden "Start Date" column after it, so a
    /// positional lookup lands on the wrong (empty) cell.
    /// </summary>
    private ILocator AccountStatusLabel(string nameFragment) =>
        Row(nameFragment).Locator("span.user-account-status-label").First;

    /// <summary>
    /// Returns the trimmed rendered status label (e.g. "Active" / "Invited" / "No account") of the
    /// "User Account" column's cell for the row matching <paramref name="nameFragment"/>. Searches
    /// first via <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync: the unfiltered
    /// page is capped, so a specific employee can silently fall outside it on this shared,
    /// long-lived E2E database.
    /// </summary>
    public async Task<string?> GetUserAccountStatusTextAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        return (await AccountStatusLabel(nameFragment).InnerTextAsync())?.Trim();
    }

    /// <summary>
    /// Returns the CSS class of the &lt;i&gt; icon rendered inside the "User Account" cell's status
    /// label for the row matching <paramref name="nameFragment"/> (e.g. "fa-solid fa-circle-check
    /// me-1" for Active) — see EmployeeList.AccountStateDisplay for the icon/status mapping this
    /// proves. Searches first via <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync.
    /// </summary>
    public async Task<string?> GetUserAccountStatusIconClassAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        var icon = AccountStatusLabel(nameFragment).Locator("i").First;
        return await icon.GetAttributeAsync("class");
    }

    /// <summary>
    /// The per-row "User Account" actions dropdown button (⋮). Rendered only when the viewer can
    /// manage user accounts (EmployeeList._canManageUserAccounts) and the row has at least one
    /// applicable action — see EmployeeList.AccountMenuItems.
    /// </summary>
    private ILocator AccountActionsButton(string nameFragment) =>
        Row(nameFragment).Locator(".user-account-actions-btn");

    /// <summary>
    /// Returns true if the row matching <paramref name="nameFragment"/> offers an "Invite" action
    /// — the redesigned User Account column (commit a80960cc) moved the old inline "Invite User"
    /// link into the ⋮ actions menu, where "Invite" appears only for "No account" rows (see
    /// EmployeeList.AccountMenuItems). Opens the menu, checks, then dismisses it. Searches first
    /// via <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync.
    /// </summary>
    public async Task<bool> HasInviteUserLinkAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        if (!await AccountActionsButton(nameFragment).First.IsVisibleAsync())
            return false;

        await AccountActionsButton(nameFragment).First.ClickAsync();
        var inviteItem = page.Locator(".e-dropdown-popup li")
            .Filter(new() { HasTextRegex = new Regex(@"^\s*Invite\s*$") });
        var present = await inviteItem.First.IsVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        return present;
    }

    /// <summary>
    /// Opens the row's ⋮ User Account actions menu and clicks its "Invite" item (present only on
    /// "No account" rows), then waits for the resulting InviteUserDialog to open.
    /// EmployeeList.OnInviteUserClicked pre-populates PreselectedEmployeeId/Name/Email, so the
    /// dialog opens directly on its single Roles + Confirm screen — there is no employee-picker
    /// step. Searches first via <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync.
    /// </summary>
    public async Task ClickInviteUserLinkAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        await AccountActionsButton(nameFragment).First.ClickAsync();
        await page.Locator(".e-dropdown-popup li")
            .Filter(new() { HasTextRegex = new Regex(@"^\s*Invite\s*$") })
            .First
            .ClickAsync();
        await InviteUserDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// The InviteUserDialog opened via <see cref="ClickInviteUserLinkAsync"/> — a single-screen
    /// dialog (no wizard steps) that always requires a pre-selected employee.
    /// </summary>
    public ILocator InviteUserDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee" });

    /// <summary>
    /// Completes the Quick Invite flow: selects the given additional role(s) (beyond the
    /// always-applied, non-selectable "Employee" role — see InviteUserDialog.razor's fixed badge)
    /// via the plain checkbox table, then confirms. Waits for the dialog to close.
    /// </summary>
    public async Task CompleteQuickInviteAsync(IReadOnlyList<string> additionalRoleNames)
    {
        foreach (var roleName in additionalRoleNames)
        {
            await InviteUserDialog.Locator("tr", new() { HasText = roleName })
                .Locator("input[type='checkbox']")
                .First
                .ClickAsync();
        }

        await InviteUserDialog.GetByRole(AriaRole.Button, new() { Name = "Send Invite" }).ClickAsync();

        await InviteUserDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    /// <summary>
    /// Returns the trimmed "Employee" summary value shown on the dialog — used to assert the
    /// pre-selected employee's name is what's actually being invited. Call after
    /// <see cref="ClickInviteUserLinkAsync"/> but before <see cref="CompleteQuickInviteAsync"/> submits.
    /// </summary>
    public async Task<string?> GetInviteDialogConfirmEmployeeNameAsync()
    {
        var dd = InviteUserDialog.Locator("dl.row dd").First;
        return (await dd.TextContentAsync())?.Trim();
    }


    // ── Search box clear / result summary ─────────────────────────────────────

    /// <summary>The "Clear search" button (only rendered while the search box has text).</summary>
    public ILocator ClearSearchButton => page.GetByRole(AriaRole.Button, new() { Name = "Clear search" });

    public async Task<bool> IsClearSearchButtonVisibleAsync() => await ClearSearchButton.IsVisibleAsync();

    /// <summary>
    /// Clicks the "Clear search" button and waits for the debounced reload (mirrors SearchAsync's
    /// own wait reasoning) so the grid settles back onto the unfiltered result set.
    /// </summary>
    public async Task ClickClearSearchAsync()
    {
        await ClearSearchButton.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task<string> GetSearchBoxValueAsync() =>
        await page.GetByPlaceholder("Search by name, email or employee number").InputValueAsync();

    /// <summary>The result-count summary line above the grid (EmployeeList._totalCount/ResultSummaryText).</summary>
    public async Task<string?> GetResultSummaryTextAsync()
    {
        var summary = page.Locator(".employee-list-summary");
        await summary.WaitForAsync(new() { Timeout = 10_000 });
        return (await summary.TextContentAsync())?.Trim();
    }

    // ── Filters panel (Department / Status) ─────────────────────────────────────

    public ILocator FiltersToggleButton => page.GetByRole(AriaRole.Button, new() { Name = "Filters" });

    public async Task OpenFiltersPanelAsync()
    {
        var panel = page.Locator(".employee-filters-panel");
        if (await panel.IsVisibleAsync())
            return;

        await FiltersToggleButton.ClickAsync();
        await panel.WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task CloseFiltersPanelAsync()
    {
        var panel = page.Locator(".employee-filters-panel");
        if (!await panel.IsVisibleAsync())
            return;

        await FiltersToggleButton.ClickAsync();
    }

    /// <summary>
    /// Selects a department in the native (non-Syncfusion) Department filter &lt;select&gt; by its
    /// visible option label, and waits for the resulting reload (OnFilterChangedAsync -> LoadAsync).
    /// </summary>
    public async Task SelectDepartmentFilterAsync(string departmentName)
    {
        await OpenFiltersPanelAsync();
        await page.Locator("#employee-filter-department").SelectOptionAsync(new SelectOptionValue { Label = departmentName });
        await page.WaitForTimeoutAsync(300);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Selects a status in the native (non-Syncfusion) Status filter &lt;select&gt; — options are the
    /// raw enum values ("Active", "Suspended", "Leaving", "FormerEmployee"), not display labels.
    /// </summary>
    public async Task SelectStatusFilterAsync(string status)
    {
        await OpenFiltersPanelAsync();
        await page.Locator("#employee-filter-status").SelectOptionAsync(new SelectOptionValue { Value = status });
        await page.WaitForTimeoutAsync(300);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task<int> GetActiveFilterCountAsync()
    {
        var badge = page.Locator(".employee-filters-count");
        if (await badge.CountAsync() == 0)
            return 0;

        var text = (await badge.First.TextContentAsync())?.Trim();
        return int.TryParse(text, out var count) ? count : 0;
    }

    public ILocator FilterChip(string label) =>
        page.Locator(".employee-filter-chip").Filter(new() { HasText = label });

    public async Task<bool> HasFilterChipAsync(string label) => await FilterChip(label).IsVisibleAsync();

    public async Task RemoveFilterChipAsync(string label)
    {
        await FilterChip(label).Locator(".employee-filter-chip-remove").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    // ── Employee identity cell / row click navigation ────────────────────────────

    /// <summary>
    /// Clicks the combined "Employee" identity cell (avatar + name + number, a single &lt;a&gt; per
    /// EmployeeList.razor's Employee GridColumn Template) for the row matching
    /// <paramref name="nameFragment"/>, and waits for navigation to that employee's profile. Distinct
    /// from clicking elsewhere in the row (see <see cref="ClickRowOutsideIdentityCellAsync"/>) so
    /// tests can independently prove both trigger navigation (OnRecordClick fires row-wide) while the
    /// checkbox column alone does not.
    /// </summary>
    public async Task ClickEmployeeIdentityCellAsync(string nameFragment)
    {
        await Row(nameFragment).Locator("a.employee-cell").First.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks a non-identity, non-checkbox cell (e.g. the "Work Email" cell) in the row matching
    /// <paramref name="nameFragment"/> to prove EmployeeList.OnRecordClick navigates from anywhere in
    /// the row, not just its own "Employee" identity link.
    /// </summary>
    public async Task ClickRowWorkEmailCellAsync(string nameFragment)
    {
        var row = Row(nameFragment);
        var cell = row.Locator(".e-rowcell").Nth(2); // 0: checkbox, 1: Employee, 2: Work Email
        await cell.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the row's own checkbox-selection cell for the row matching <paramref name="nameFragment"/>
    /// — per EmployeeList.OnRecordClick, this must toggle selection only and must NOT navigate.
    /// </summary>
    public async Task ClickRowCheckboxCellAsync(string nameFragment)
    {
        await Row(nameFragment).Locator(".e-checkbox-wrapper").First.ClickAsync();
    }

    // ── Selected-count label on "Update selected" ───────────────────────────────

    /// <summary>Reads the accessible name of the "Update selected" toolbar button, e.g. "Update selected (2)".</summary>
    public async Task<string?> GetUpdateSelectedButtonTextAsync()
    {
        var button = page.Locator("button").Filter(new() { HasText = "Update selected" }).First;
        return (await button.TextContentAsync())?.Trim();
    }

    /// <summary>Unchecks a previously-checked row's checkbox (same click target as CheckEmployeeRowAsync).</summary>
    public async Task UncheckEmployeeRowAsync(string nameFragment) => await CheckEmployeeRowAsync(nameFragment);
}
