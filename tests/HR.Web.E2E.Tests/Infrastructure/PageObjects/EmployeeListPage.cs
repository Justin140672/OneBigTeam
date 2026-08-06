using System.Text.RegularExpressions;
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
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/employees/new", new() { Timeout = 15_000 });
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
        await row.Locator(".e-checkbox-wrapper").First.ClickAsync();
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
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
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
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
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
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// The page's own error banner (_actionError), e.g. shown if downloading the compensation
    /// import template fails.
    /// </summary>
    public async Task<string?> GetActionErrorMessageAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown (BulkUpdateMenu) and clicks "Download Template",
    /// triggering a browser download of the compensation import template — mirrors
    /// BulkCompensationUpdatePage.ClickDownloadTemplateAsync for the full-page equivalent.
    /// </summary>
    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await OpenBulkUpdateMenuItemAsync("Download Template");
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
        await OpenBulkUpdateMenuItemAsync("Import", exact: true);
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-import-dialog",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the "Bulk Update" toolbar button (BulkUpdateMenu.razor's SfDropDownButton) and then
    /// the named menu item within the popup it opens. A single click on a just-mounted
    /// SfDropDownButton can land before Syncfusion's JS interop has attached its click listener —
    /// the click is silently swallowed, no popup ever opens, and the follow-up item click then
    /// waits the full default timeout for an item that will never appear (same class of race
    /// DropDownSelector.SelectAsync guards against for SfDropDownList combo boxes). Retries the
    /// button click a few times, but only when the popup itself never opened at all — checked via
    /// its own ".e-dropdown-popup" container rather than the specific item, since re-clicking the
    /// trigger while the popup IS already open toggles a SfDropDownButton closed again (unlike
    /// SfDropDownList's combobox, which stays open on a same-target re-click) — retrying past that
    /// point would just flap the menu open/closed and never let a genuinely-slow-to-render item
    /// catch up.
    /// </summary>
    private async Task OpenBulkUpdateMenuItemAsync(string itemName, bool exact = false)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" });
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

        var menuItem = page.GetByRole(AriaRole.Menuitem, new() { Name = itemName, Exact = exact });
        await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await menuItem.ClickAsync();
    }

    public async Task ClickEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        // Only the Last Name column cell renders as an <a> (EmployeeList.razor's Last Name
        // GridColumn Template — First Name is plain text), so a link's own text is never more
        // than the last name alone. Find the row first (order-agnostic across its Last/First Name
        // cells — see NameMatcher), then click that row's link rather than filtering the link
        // itself by the full nameFragment.
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
    /// Returns the trimmed rendered text (icon label, e.g. "Active"/"Pending Invitation"/"No
    /// User") of the "User Account" column's cell for the row matching <paramref name="nameFragment"/>.
    /// The column is the last one in EmployeeList.razor's GridColumns, so its cell is the last
    /// ".e-rowcell" in the row. Searches first via <see cref="SearchAsync"/> — same reasoning as
    /// HasEmployeeAsync: the unfiltered page is capped at 100 rows, so a specific employee can
    /// silently fall outside it on this shared, long-lived E2E database.
    /// </summary>
    public async Task<string?> GetUserAccountStatusTextAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        // The cell can also contain a trailing "Invite User" action link (rendered only for
        // "NoUser" rows — see EmployeeList.razor's User Account GridColumn Template). Reading the
        // whole cell's InnerText would concatenate that link's text onto the status label (e.g.
        // "No UserInvite User"). Read only the status label <span> so callers get just the status.
        var label = Row(nameFragment).Locator(".e-rowcell").Last.Locator("span.user-account-status-label").First;
        return (await label.InnerTextAsync())?.Trim();
    }

    /// <summary>
    /// Returns the CSS class of the &lt;i&gt; icon rendered in the "User Account" cell for the row
    /// matching <paramref name="nameFragment"/> (e.g. "fa-solid fa-circle-check me-1" for Active) —
    /// see EmployeeList.UserAccountStatusDisplay for the icon/status mapping this proves. Searches
    /// first via <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync.
    /// </summary>
    public async Task<string?> GetUserAccountStatusIconClassAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        var icon = Row(nameFragment).Locator(".e-rowcell").Last.Locator("i").First;
        return await icon.GetAttributeAsync("class");
    }

    /// <summary>
    /// Returns true if the row-level "Invite User" link (rendered only when
    /// UserAccountStatus == "NoUser" — see EmployeeList.razor's User Account GridColumn Template)
    /// is visible for the row matching <paramref name="nameFragment"/>. Searches first via
    /// <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync.
    /// </summary>
    public async Task<bool> HasInviteUserLinkAsync(string nameFragment)
    {
        return await Row(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Invite User" }).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the row-level "Invite User" link for the row matching <paramref name="nameFragment"/>
    /// (only present on "No User" rows) and waits for the resulting InviteUserDialog to open.
    /// Because EmployeeList.OnInviteUserClicked pre-populates PreselectedEmployeeId/Name/Email, the
    /// dialog opens straight on step 2 ("Email & Roles") rather than the employee-picker step.
    /// Searches first via <see cref="SearchAsync"/> — same reasoning as HasEmployeeAsync.
    /// </summary>
    public async Task ClickInviteUserLinkAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        await Row(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Invite User" }).ClickAsync();
        await InviteUserDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// The InviteUserDialog opened via <see cref="ClickInviteUserLinkAsync"/> — same component as
    /// UserAdministrationListPage's invite wizard (shares the "Invite Employee" dialog title), just
    /// parameterised with PreselectedEmployeeId/Name/Email so it skips straight to step 2 (Roles).
    /// </summary>
    public ILocator InviteUserDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee" });

    /// <summary>
    /// Returns true if the (open) InviteUserDialog is still showing the step-1 employee-picker
    /// combobox — expected to be false for the pre-selected Quick Invite flow, which jumps
    /// straight to step 2 and never renders the picker at all.
    /// </summary>
    public async Task<bool> InviteDialogHasEmployeePickerAsync() =>
        await InviteUserDialog.Locator("span[role='combobox']").CountAsync() > 0;

    /// <summary>
    /// Returns true if the (open) InviteUserDialog's step nav shows an "Employee" step pill —
    /// expected false for the pre-selected Quick Invite flow, which hides that step entirely (see
    /// InviteUserDialog.razor's VisibleSteps, gated on PreselectedEmployeeId being set).
    /// </summary>
    public Task<bool> InviteDialogHasEmployeeStepPillAsync() =>
        InviteUserDialog.Locator(".nav-link", new() { HasText = "Employee" }).IsVisibleAsync();

    /// <summary>
    /// Completes the pre-selected Quick Invite flow from step 2 (Roles) onward: selects the given
    /// additional role(s) (beyond the always-applied, non-selectable "Employee" role — see
    /// InviteUserDialog.razor's fixed badge) via the SfMultiSelect checkbox popup, advances to step
    /// 3 (Confirm), and submits. Mirrors the step 2/3 portion of
    /// UserAdministrationListPage.InviteEmployeeAsync (same InviteUserDialog component, same
    /// Syncfusion widgets/interaction patterns), but skips step 1 entirely since the employee is
    /// already pre-selected. Waits for the dialog to close.
    /// </summary>
    public async Task CompleteQuickInviteAsync(IReadOnlyList<string> additionalRoleNames)
    {
        if (additionalRoleNames.Count > 0)
        {
            await InviteUserDialog.Locator("input[placeholder='Select additional roles (optional)']").ClickAsync();
            await page.WaitForSelectorAsync(".e-popup:visible", new() { Timeout = 10_000 });
            foreach (var roleName in additionalRoleNames)
            {
                await page.Locator(".e-popup .e-list-item")
                    .Filter(new() { HasText = roleName })
                    .First
                    .ClickAsync();
            }
            // Checkbox-mode multiselect popups stay open to allow further selections, and being an
            // overlay it can sit visually on top of (and intercept clicks intended for) whatever sits
            // beneath it, including the dialog's own "Next" button — clicking a "neutral" element
            // underneath it is not reliable. Escape is the standard, unambiguous way to close a
            // Syncfusion dropdown/multiselect popup without depending on its rendered size/position —
            // same reasoning as UserDetailPage.ToggleRolesAndSaveAsync for the equivalent widget.
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForSelectorAsync(".e-popup:visible", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }

        await InviteUserDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // Step 3: Confirm.
        await InviteUserDialog.GetByRole(AriaRole.Button, new() { Name = "Send Invite" }).ClickAsync();

        await InviteUserDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    /// <summary>
    /// Returns the trimmed "Employee" summary value shown on the dialog's step 3 (Confirm) screen —
    /// used to assert the pre-selected employee's name is what's actually being invited, not a
    /// picker-driven selection. Call after advancing to step 3 but before
    /// <see cref="CompleteQuickInviteAsync"/> submits.
    /// </summary>
    public async Task<string?> GetInviteDialogConfirmEmployeeNameAsync()
    {
        var dd = InviteUserDialog.Locator("dl.row dd").First;
        return (await dd.TextContentAsync())?.Trim();
    }

    // ── Sorting ────────────────────────────────────────────────────────────────

    private ILocator UserAccountHeaderCell =>
        page.Locator(".e-headercell").Filter(new() { HasText = "User Account" });

    /// <summary>
    /// Clicks the "User Account" column header (standard EJ2 single-column sort click target —
    /// same pattern as SharedDocumentSortByReviewDateTests' ClickReviewDateHeaderAsync) and
    /// best-effort waits for the sort-indicator class. Row-order assertions in tests are the
    /// actual source of truth regardless of whether this wait's indicator-class assumption holds.
    /// </summary>
    public async Task ClickUserAccountHeaderAsync(string expectedDirectionClass)
    {
        await UserAccountHeaderCell.Locator(".e-headercelldiv").First.ClickAsync();
        try
        {
            await page.WaitForSelectorAsync(
                $".e-headercell.{expectedDirectionClass}:has-text('User Account')",
                new() { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await page.WaitForTimeoutAsync(500);
        }
    }

    /// <summary>
    /// Reads every visible ".e-row"'s "User Account" cell text (last ".e-rowcell") in DOM order —
    /// used to prove the column is genuinely sortable (values move from ascending to descending
    /// order relative to each other) without needing to know exactly which employees/statuses are
    /// on the shared, long-lived E2E database at any given time.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVisibleUserAccountStatusesInOrderAsync()
    {
        var rows = page.Locator(".e-grid .e-row");
        var count = await rows.CountAsync();
        var result = new List<string>();
        for (var i = 0; i < count; i++)
        {
            // Read only the status label <span> (see GetUserAccountStatusTextAsync above) —
            // the cell's full InnerText would concatenate the "NoUser" row's trailing "Invite
            // User" action-link text onto the label with no separator (e.g. "No UserInvite User"),
            // breaking exact-match comparisons like the Excel-filter test's Assert.Equal.
            var label = rows.Nth(i).Locator(".e-rowcell").Last.Locator("span.user-account-status-label").First;
            result.Add((await label.InnerTextAsync()).Trim());
        }
        return result;
    }

    // ── Column (Excel-style) filter ───────────────────────────────────────────
    //
    // The global "Search by name, email or employee number" textbox is server-side and only
    // matches FirstName/LastName/WorkEmail/EmployeeNumber (see ListEmployeesHandler) — it does not
    // and cannot filter on User Account status. The mechanism every grid column (including this
    // one) actually participates in is HrGrid's own per-column Excel-style filter
    // (AllowFiltering="true" + GridFilterSettings { Type = FilterType.Excel } — see HrGrid.cs),
    // triggered via the small filter icon Syncfusion renders in each header cell.

    /// <summary>Opens the "User Account" column's Excel-style filter popup.</summary>
    public async Task OpenUserAccountColumnFilterAsync()
    {
        await UserAccountHeaderCell.Locator(".e-filtermenudiv").ClickAsync();
        await page.WaitForSelectorAsync(".e-excelfilter:visible, .e-flmenu:visible", new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Ticks the checkbox for <paramref name="valueLabel"/> (e.g. "No User") — and only that
    /// value — in the (already open) Excel filter popup and applies it via the popup's own "OK"
    /// button, then waits for the grid to settle on the filtered result. An unfiltered column's
    /// popup opens with every value (and "Select All") already checked, since "all checked" is
    /// what an unfiltered/show-everything state means — so a single click on
    /// <paramref name="valueLabel"/> alone would *uncheck* it (filtering it out, leaving every
    /// other value still selected) rather than isolating it. Unchecking "Select All" first clears
    /// every value, then the search narrows the list so the one remaining click checks only
    /// <paramref name="valueLabel"/> back on.
    /// </summary>
    public async Task ApplyUserAccountColumnFilterAsync(string valueLabel)
    {
        var selectAllCheckbox = page.Locator(".e-excelfilter:visible .e-ftrchk")
            .Filter(new() { HasText = "Select All" })
            .First
            .Locator(".e-frame, .e-checkbox-wrapper")
            .First;
        if (await selectAllCheckbox.CountAsync() > 0)
            await selectAllCheckbox.ClickAsync();

        var searchInput = page.Locator(".e-excelfilter:visible .e-searchinput input");
        if (await searchInput.CountAsync() > 0)
            await searchInput.FillAsync(valueLabel);

        await page.Locator(".e-excelfilter:visible .e-ftrchk")
            .Filter(new() { HasText = valueLabel })
            .First
            .ClickAsync();

        await page.Locator(".e-excelfilter:visible button")
            .Filter(new() { HasText = "OK" })
            .First
            .ClickAsync();

        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Clears any active filter on the "User Account" column via the header's clear-filter icon.</summary>
    public async Task ClearUserAccountColumnFilterAsync()
    {
        await OpenUserAccountColumnFilterAsync();

        var okButton = page.Locator(".e-excelfilter:visible button").Filter(new() { HasText = "OK" }).First;

        // Reopening the popup after a narrowed filter (e.g. only "No User" checked) leaves "Select
        // All" indeterminate, and OK stays disabled until at least one value is checked. A single
        // click on the row is not always reliable at hitting the actual checkbox input rather than
        // its label/wrapper, so click the checkbox frame itself and poll for OK to become enabled,
        // retrying the click if the first one didn't register.
        var selectAllCheckbox = page.Locator(".e-excelfilter:visible .e-ftrchk")
            .Filter(new() { HasText = "Select All" })
            .First
            .Locator(".e-frame, .e-checkbox-wrapper")
            .First;

        if (await selectAllCheckbox.CountAsync() > 0)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (await okButton.IsEnabledAsync())
                    break;

                await selectAllCheckbox.ClickAsync();
                try
                {
                    await okButton.WaitForAsync(new() { Timeout = 2_000 });
                    if (await okButton.IsEnabledAsync())
                        break;
                }
                catch (TimeoutException)
                {
                    // fall through and retry the click
                }
            }
        }

        await okButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }
}
