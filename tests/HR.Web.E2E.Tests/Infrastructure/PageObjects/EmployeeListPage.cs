using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeListPage(IPage page, string baseUrl)
{
    private static Regex NameMatcher(string nameFragment)
    {
        var words = nameFragment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lookaheads = string.Concat(words.Select(w => $"(?=.*{Regex.Escape(w)})"));
        return new Regex(lookaheads, RegexOptions.Singleline);
    }

    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewEmployeeAsync()
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Add employee" });
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        // Under severe transient load a genuinely-working click can still take well past a single
        // attempt's window even after several retries — one observed run exhausted a 7×5s + 30s
        // budget entirely and still failed. Give the last few attempts real headroom (not just the
        // very last one) rather than assuming one long attempt is always enough.
        const int maxAttempts = 12;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var isLateAttempt = attempt > maxAttempts - 3;
            try
            {
                // ClickAsync must be inside the try too, not just the URL wait — it has no
                // explicit Timeout of its own, so it uses Playwright's default 30s actionability
                // wait. If the button is momentarily non-actionable (a transient overlay,
                // mid-render state) on an early attempt, an unwrapped ClickAsync would throw and
                // escape the loop immediately, defeating the retry entirely — which is exactly
                // what an unretried "Timeout 30000ms exceeded" failure here looks like.
                await button.ClickAsync(new() { Timeout = isLateAttempt ? 20_000 : 5_000 });
                await page.WaitForURLAsync("**/employees/new**", new() { Timeout = isLateAttempt ? 15_000 : 3_000 });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
        }
    }

    public async Task<bool> HasEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var searchInput = page.GetByPlaceholder("Search by name, email or employee number");
        await searchInput.FillAsync(nameFragment);
        await searchInput.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

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

    public async Task CheckEmployeeRowAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-grid .e-row").Filter(new() { HasTextRegex = NameMatcher(nameFragment) }).First;
        if (await row.CountAsync() == 0)
        {
            await SearchAsync(nameFragment);
            row = page.Locator(".e-grid .e-row").Filter(new() { HasTextRegex = NameMatcher(nameFragment) }).First;
        }
        var checkbox = row.Locator(".e-checkbox-wrapper").First;
        var input = checkbox.Locator("input[type='checkbox']").First;
        var wasChecked = await input.IsCheckedAsync();

        await checkbox.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await input.IsCheckedAsync() == wasChecked && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    public async Task<bool> IsBulkUpdateButtonDisabledAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Update selected" }).ClickAsync();
        var item = page.Locator("#hr-bulk-selected");
        await item.WaitForAsync(new() { Timeout = 10_000 });

        string? ariaDisabled = null;
        bool hasDisabledClass = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var previous = (string?)null;
        while (true)
        {
            ariaDisabled = await item.GetAttributeAsync("aria-disabled");
            hasDisabledClass = (await item.GetAttributeAsync("class"))?.Contains("e-disabled") ?? false;
            var current = ariaDisabled == "true" || hasDisabledClass ? "disabled" : "enabled";
            if (previous == current || DateTime.UtcNow >= deadline) break;
            previous = current;
            await page.WaitForTimeoutAsync(150);
        }

        await page.Keyboard.PressAsync("Escape");
        try
        {
            await page.Locator(".e-dropdown-popup").WaitForAsync(
                new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
        }
        catch (TimeoutException)
        {
        }

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
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Update selected" });
        var popup = page.Locator(".e-dropdown-popup");
        var item = page.Locator("#hr-bulk-selected");

        if (await popup.IsVisibleAsync())
        {
            await page.Keyboard.PressAsync("Escape");
            try { await popup.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 }); }
            catch (TimeoutException) { }
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await button.ClickAsync();
            try
            {
                await popup.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < 5 ? 2_500 : 10_000 });
                await item.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < 5 ? 2_500 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 5)
            {
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(400);
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var stillDisabled = (await item.GetAttributeAsync("class"))?.Contains("e-disabled") ?? false;
            if (!stillDisabled) break;
            await page.WaitForTimeoutAsync(150);
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await item.ClickAsync(new() { Timeout = attempt < 3 ? 5_000 : 15_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
            }
        }

        var dialog = page.Locator("[role='dialog'].bulk-compensation-update-dialog");
        var selectionError = page.Locator(".alert-danger").Filter(new() { HasText = "Select at least one employee" });
        await dialog.Or(selectionError).First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        if (await selectionError.IsVisibleAsync())
            throw new InvalidOperationException(
                "Bulk update did not open: the employee grid had no selected rows when 'Selected Employees' was clicked.");
    }

    public async Task<string?> GetActionSuccessMessageAsync()
    {
        var banner = page.Locator(".alert-success");
        return await banner.WaitUntilVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetActionErrorMessageAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.WaitUntilVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await OpenBulkUpdateMenuItemAsync("hr-bulk-download-template");
        var download = await downloadTask;
        return download.SuggestedFilename;
    }

    public async Task ClickBulkImportAsync()
    {
        await OpenBulkUpdateMenuItemAsync("hr-bulk-import");
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-import-dialog",
            new() { Timeout = 15_000 });
    }

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
            }
        }

        var menuItem = page.Locator($"#{itemId}");
        await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await menuItem.ClickAsync();
    }

    public async Task ClickEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await SearchAsync(nameFragment);

        var link = page.Locator(".e-grid .e-row")
            .Filter(new() { HasTextRegex = NameMatcher(nameFragment) })
            .First
            .Locator(".e-rowcell a")
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 15_000 });
    }

    public async Task SearchAsync(string query)
    {
        var searchInput = page.GetByPlaceholder("Search by name, email or employee number");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        await searchInput.PressAsync("Enter");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

            if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
                break;

            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rowTexts = await page.Locator(".e-grid .e-row").AllTextContentsAsync();
            if (rowTexts.Count > 0 && rowTexts.All(t =>
                    queryWords.All(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
                break;

            if (DateTime.UtcNow >= deadline)
                break;

            await page.WaitForTimeoutAsync(200);
        }
    }


    private ILocator Row(string nameFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasTextRegex = NameMatcher(nameFragment) }).First;

    private ILocator AccountStatusLabel(string nameFragment) =>
        Row(nameFragment).Locator("span.user-account-status-label").First;

    public async Task<string?> GetUserAccountStatusTextAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        return (await AccountStatusLabel(nameFragment).InnerTextAsync())?.Trim();
    }

    public async Task<string?> GetUserAccountStatusIconClassAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);
        var icon = AccountStatusLabel(nameFragment).Locator("i").First;
        return await icon.GetAttributeAsync("class");
    }

    private ILocator AccountActionsButton(string nameFragment) =>
        Row(nameFragment).Locator(".user-account-actions-btn");

    public async Task<bool> HasInviteUserLinkAsync(string nameFragment)
    {
        await SearchAsync(nameFragment);

        try
        {
            await AccountActionsButton(nameFragment).First.WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return false;
        }

        await AccountActionsButton(nameFragment).First.ClickAsync();
        var inviteItem = page.Locator(".e-dropdown-popup li")
            .Filter(new() { HasTextRegex = new Regex(@"^\s*Invite\s*$") });
        bool present;
        try
        {
            await inviteItem.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            present = true;
        }
        catch (TimeoutException)
        {
            present = false;
        }
        await page.Keyboard.PressAsync("Escape");
        return present;
    }

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

    public ILocator InviteUserDialog =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee" });

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

    public async Task<string?> GetInviteDialogConfirmEmployeeNameAsync()
    {
        var dd = InviteUserDialog.Locator("dl.row dd").First;
        return (await dd.TextContentAsync())?.Trim();
    }



    public ILocator ClearSearchButton => page.GetByRole(AriaRole.Button, new() { Name = "Clear search" });

    public async Task<bool> IsClearSearchButtonVisibleAsync()
    {
        try
        {
            await ClearSearchButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ClickClearSearchAsync()
    {
        await ClearSearchButton.ClickAsync();
        await Assertions.Expect(page.GetByPlaceholder("Search by name, email or employee number"))
            .ToHaveValueAsync("", new() { Timeout = 5_000 });
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task<string> GetSearchBoxValueAsync() =>
        await page.GetByPlaceholder("Search by name, email or employee number").InputValueAsync();

    public async Task<string?> GetResultSummaryTextAsync()
    {
        var summary = page.Locator(".employee-list-summary");
        await summary.WaitForAsync(new() { Timeout = 10_000 });
        return (await summary.TextContentAsync())?.Trim();
    }


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

    public async Task SelectDepartmentFilterAsync(string departmentName)
    {
        await OpenFiltersPanelAsync();
        await page.Locator("#employee-filter-department").SelectOptionAsync(new SelectOptionValue { Label = departmentName });
        await page.WaitForTimeoutAsync(300);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

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


    public async Task ClickEmployeeIdentityCellAsync(string nameFragment)
    {
        await Row(nameFragment).Locator("a.employee-cell").First.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
    }

    public async Task ClickRowWorkEmailCellAsync(string nameFragment)
    {
        var row = Row(nameFragment);
        var cell = row.Locator(".e-rowcell").Nth(2);
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


    public async Task<string?> GetUpdateSelectedButtonTextAsync()
    {
        var button = page.Locator("button").Filter(new() { HasText = "Update selected" }).First;
        return (await button.TextContentAsync())?.Trim();
    }

    public async Task UncheckEmployeeRowAsync(string nameFragment) => await CheckEmployeeRowAsync(nameFragment);


    public async Task GoToInviteModeAsync(Guid companyId, string? returnUrl = null)
    {
        var url = $"{baseUrl}/companies/{companyId}/employees?mode=invite";
        if (returnUrl is not null)
            url += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync(
            ".invite-mode-grid .e-row, .invite-mode-grid .e-emptyrow",
            new() { Timeout = 20_000 });
    }

    public ILocator InviteModeBanner => page.Locator(".invite-mode-banner");

    public Task<bool> IsInviteModeBannerVisibleAsync() => InviteModeBanner.IsVisibleAsync();

    public async Task<string?> GetBackToListLinkHrefAsync() =>
        await InviteModeBanner.GetByRole(AriaRole.Link, new() { Name = "Back to employee list" }).GetAttributeAsync("href");

    public ILocator InviteSelectedToolbarButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Invite selected" });

    public Task<bool> IsInviteSelectedToolbarButtonDisabledAsync() => InviteSelectedToolbarButton.IsDisabledAsync();

    public async Task<bool> WaitForInviteSelectedToolbarButtonEnabledAsync()
    {
        try
        {
            await Assertions.Expect(InviteSelectedToolbarButton).ToBeEnabledAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public Task ClickInviteSelectedToolbarButtonAsync() => InviteSelectedToolbarButton.ClickAsync();

    /// <summary>
    /// Ticket 9: the warning shown after a batch is queued listing the selected employees the
    /// server excluded (e.g. reason PublicEmailDomain) — heading "{N} employee(s) were not invited:".
    /// </summary>
    public ILocator InviteBatchExcludedAlert => page.Locator("[data-testid='invite-batch-excluded']");

    public ILocator InviteBatchExcludedRows =>
        InviteBatchExcludedAlert.Locator("[data-testid='invite-batch-excluded-row']");
}
