using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee list page (/companies/{companyId}/employees).
/// </summary>
public sealed class EmployeeListPage(IPage page, string baseUrl)
{
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

        await page.GetByPlaceholder("Search by name, email or employee number").FillAsync(nameFragment);
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();
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

        var row = page.Locator(".e-grid .e-row").Filter(new() { HasText = nameFragment }).First;
        await row.Locator(".e-checkbox-wrapper").First.ClickAsync();
    }

    /// <summary>
    /// Returns true if the "Bulk Update" toolbar dropdown button (id "hr-bulk-update", added via
    /// EmployeeList.ConfigureToolbar, now rendered as a BulkUpdateMenu SfDropDownButton via
    /// EmployeeList.EmployeeToolbar) is currently disabled — it tracks row selection the same way
    /// as the built-in Edit/View actions (SearchPageBase.OnRowSelected/OnRowDeselected), so it's
    /// disabled with zero rows selected and enabled once 1+ rows are selected.
    /// </summary>
    public async Task<bool> IsBulkUpdateButtonDisabledAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" });
        return await btn.IsDisabledAsync();
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
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Download Template" }).ClickAsync();
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
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Import", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-import-dialog",
            new() { Timeout = 15_000 });
    }

    public async Task ClickEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
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
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    // ── User Account column (tickets #90/#91 — "User Account" status + Quick Invite) ──────────

    /// <summary>
    /// Returns the row matching <paramref name="nameFragment"/> in the last name/first name cells
    /// — used as the anchor for all of the User-Account-column helpers below.
    /// </summary>
    private ILocator Row(string nameFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasText = nameFragment }).First;

    /// <summary>
    /// Returns the trimmed rendered text (icon label, e.g. "Active"/"Pending Invitation"/"No
    /// User") of the "User Account" column's cell for the row matching <paramref name="nameFragment"/>.
    /// The column is the last one in EmployeeList.razor's GridColumns, so its cell is the last
    /// ".e-rowcell" in the row.
    /// </summary>
    public async Task<string?> GetUserAccountStatusTextAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var cell = Row(nameFragment).Locator(".e-rowcell").Last;
        return (await cell.InnerTextAsync())?.Trim();
    }

    /// <summary>
    /// Returns the CSS class of the &lt;i&gt; icon rendered in the "User Account" cell for the row
    /// matching <paramref name="nameFragment"/> (e.g. "fa-solid fa-circle-check me-1" for Active) —
    /// see EmployeeList.UserAccountStatusDisplay for the icon/status mapping this proves.
    /// </summary>
    public async Task<string?> GetUserAccountStatusIconClassAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var icon = Row(nameFragment).Locator(".e-rowcell").Last.Locator("i").First;
        return await icon.GetAttributeAsync("class");
    }

    /// <summary>
    /// Returns true if the row-level "Invite User" link (rendered only when
    /// UserAccountStatus == "NoUser" — see EmployeeList.razor's User Account GridColumn Template)
    /// is visible for the row matching <paramref name="nameFragment"/>.
    /// </summary>
    public async Task<bool> HasInviteUserLinkAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await Row(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Invite User" }).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the row-level "Invite User" link for the row matching <paramref name="nameFragment"/>
    /// (only present on "No User" rows) and waits for the resulting InviteUserDialog to open.
    /// Because EmployeeList.OnInviteUserClicked pre-populates PreselectedEmployeeId/Name/Email, the
    /// dialog opens straight on step 2 ("Email & Roles") rather than the employee-picker step.
    /// </summary>
    public async Task ClickInviteUserLinkAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await Row(nameFragment).GetByRole(AriaRole.Link, new() { Name = "Invite User" }).ClickAsync();
        await InviteUserDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// The InviteUserDialog opened via <see cref="ClickInviteUserLinkAsync"/> — same component as
    /// UserAdministrationListPage's invite wizard (shares the "Invite Employee User" dialog title),
    /// just parameterised with PreselectedEmployeeId/Name/Email so it skips straight to step 2.
    /// </summary>
    public ILocator InviteUserDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee User" });

    /// <summary>
    /// Returns true if the (open) InviteUserDialog is still showing the step-1 employee-picker
    /// combobox — expected to be false for the pre-selected Quick Invite flow, which jumps
    /// straight to step 2 and never renders the picker at all.
    /// </summary>
    public async Task<bool> InviteDialogHasEmployeePickerAsync() =>
        await InviteUserDialog.Locator("span[role='combobox']").CountAsync() > 0;

    /// <summary>Returns the current value of the (pre-filled) Email field on the dialog's step 2.</summary>
    public async Task<string?> GetInviteDialogEmailValueAsync() =>
        await InviteUserDialog.Locator("input[placeholder='work@company.com']").InputValueAsync();

    /// <summary>
    /// Completes the pre-selected Quick Invite flow from step 2 (Email & Roles) onward: selects
    /// the given role(s) via the SfMultiSelect checkbox popup, advances to step 3 (Confirm), and
    /// submits. Mirrors the step 2/3 portion of UserAdministrationListPage.InviteEmployeeAsync
    /// (same InviteUserDialog component, same Syncfusion widgets/interaction patterns), but skips
    /// step 1 entirely since the employee is already pre-selected. Waits for the dialog to close.
    /// </summary>
    public async Task CompleteQuickInviteAsync(IReadOnlyList<string> roleNames)
    {
        await InviteUserDialog.Locator("input[placeholder='Select one or more roles']").ClickAsync();
        await page.WaitForSelectorAsync(".e-popup:visible", new() { Timeout = 10_000 });
        foreach (var roleName in roleNames)
        {
            await page.Locator(".e-popup .e-list-item")
                .Filter(new() { HasText = roleName })
                .First
                .ClickAsync();
        }
        // Checkbox-mode multiselect popup stays open — click a neutral area of the dialog (the
        // nav-pills step header) to close it via its outside-click handler before reaching Next.
        await InviteUserDialog.Locator("ul.nav-pills").ClickAsync();

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
            result.Add((await rows.Nth(i).Locator(".e-rowcell").Last.InnerTextAsync()).Trim());
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
    /// Ticks the checkbox for <paramref name="valueLabel"/> (e.g. "No User") in the (already open)
    /// Excel filter popup and applies it via the popup's own "OK" button, then waits for the grid
    /// to settle on the filtered result.
    /// </summary>
    public async Task ApplyUserAccountColumnFilterAsync(string valueLabel)
    {
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
        var clearAll = page.Locator(".e-excelfilter:visible .e-ftrchk")
            .Filter(new() { HasText = "Select All" })
            .First;
        if (await clearAll.CountAsync() > 0)
            await clearAll.ClickAsync();
        await page.Locator(".e-excelfilter:visible button")
            .Filter(new() { HasText = "OK" })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }
}
