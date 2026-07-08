using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class MyProfilePage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile");
        // Poll until the circuit is connected and data loaded: grid visible, no skeleton.
        await page.EvaluateAsync(@"() => {
            window._profileReady = false;
            const poll = setInterval(() => {
                if (!document.querySelector('.overview-skeleton') &&
                    document.querySelector('.overview-grid')) {
                    window._profileReady = true;
                    clearInterval(poll);
                }
            }, 500);
        }");
        await page.WaitForFunctionAsync(
            "window._profileReady === true",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
    }

    /// <summary>Waits for the profile page to load after a redirect (e.g. from "View all" link).</summary>
    public async Task WaitForLoadAsync()
    {
        await page.WaitForSelectorAsync(".e-tab", new() { Timeout = 20_000 });
    }

    /// <summary>Returns the name of the currently active tab.</summary>
    public async Task<string> GetActiveTabNameAsync()
    {
        var active = page.Locator("[role='tab'][aria-selected='true']").First;
        await active.WaitForAsync(new() { Timeout = 10_000 });
        return (await active.TextContentAsync())?.Trim() ?? "";
    }

    // ── Tab navigation ────────────────────────────────────────────────────────

    public async Task OpenOverviewTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Overview" }).ClickAsync();
        await page.WaitForSelectorAsync(".overview-grid, .overview-skeleton, .alert",
            new() { Timeout = 15_000 });
    }

    public async Task OpenContactDetailsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Contact Details" }).ClickAsync();
        await page.WaitForSelectorAsync(".cd-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenPersonalDetailsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Personal Details" }).ClickAsync();
        await page.WaitForSelectorAsync(".pd-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenEmergencyContactsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Emergency Contacts" }).ClickAsync();
        await page.WaitForSelectorAsync(".ec-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Documents" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    public async Task OpenTasksTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, p", new() { Timeout = 15_000 });
    }

    public async Task OpenAssetsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Assets" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, .spinner-border", new() { Timeout = 15_000 });
        // Wait for the spinner to disappear before asserting grid content.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Returns true if the assets grid has at least one data row.</summary>
    public async Task<bool> HasAssetsTableAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync() > 0;

    /// <summary>Returns all asset numbers from the first column (Asset) in the assets grid.</summary>
    public async Task<IReadOnlyList<string>> GetAssetNumbersAsync()
    {
        var spans = await page.Locator(".e-grid .e-row .e-rowcell:first-child .fw-medium").AllTextContentsAsync();
        return spans.Select(t => t.Trim()).ToList();
    }

    /// <summary>Returns the number of data rows in the assets grid.</summary>
    public async Task<int> GetAssetRowCountAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync();

    // ── Leave tab ─────────────────────────────────────────────────────────────

    public async Task OpenLeaveTabAsync()
    {
        var leaveTab = page.GetByRole(AriaRole.Tab, new() { Name = "Leave" });
        await leaveTab.ClickAsync();
        // Wait for the balance data to load from the API — the Annual Leave card's value must
        // be present before we proceed. Waiting for just .card-header is not enough because
        // that element renders before the async balance fetch completes, leaving _allBalances
        // empty and the leave-type dropdown with no items when the dialog opens.
        await page.Locator(".card").Filter(new() { HasText = "Annual Leave" })
            .Locator("dd.fs-4.fw-semibold")
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Reads the Annual Leave remaining balance from the balance card as a decimal, regardless
    /// of the unit the UI currently renders in. Historically this was "25 days"/"20.5 days"; the
    /// Leave Balance Adjustment feature changed the self-service display to hours (e.g. "112.5h",
    /// no space before the suffix). Rather than depending on either exact format, this extracts
    /// the leading numeric portion so relative before/after comparisons in existing tests
    /// (LeaveApprovalTests, LeaveRejectionTests, LeaveCancellationTests) keep working regardless
    /// of the unit — those tests only assert equality/direction of change, never an absolute
    /// day count, so comparing the hour-denominated value is equally valid for their purposes.
    /// Returns null if the card is not yet visible or the text has no leading number.
    /// </summary>
    public async Task<decimal?> GetAnnualLeaveRemainingAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd   = card.Locator("dd.fs-4.fw-semibold");
        // Wait for the balance to load — it arrives via an async API call after the tab renders.
        await dd.WaitForAsync(new() { Timeout = 20_000 });
        var text = (await dd.TextContentAsync())?.Trim() ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[\d.]+");
        return match.Success && decimal.TryParse(match.Value, out var v) ? v : null;
    }

    /// <summary>
    /// Returns the raw rendered text of the Annual Leave "Remaining" value on the self-service
    /// Leave tab (e.g. "112.5h"), without attempting to parse it to a number. Prefer this over
    /// the (days-format) <see cref="GetAnnualLeaveRemainingAsync"/> helper when asserting on the
    /// hours-based display introduced by the Leave Balance Adjustment feature.
    /// </summary>
    public async Task<string?> GetAnnualLeaveRemainingTextAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd = card.Locator("dd.fs-4.fw-semibold");
        await dd.WaitForAsync(new() { Timeout = 20_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns true if any "Adjust" button is visible anywhere on the self-service Leave tab.
    /// There must never be one here — balance adjustments are an HR/admin-only action performed
    /// from the admin employee edit page, not from an employee's own "My Profile" view.
    /// </summary>
    public async Task<bool> HasAnyAdjustButtonOnLeaveTabAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).CountAsync() > 0;

    /// <summary>Opens the Request Leave dialog.</summary>
    public async Task ClickRequestLeaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Leave" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the status badge text for the leave request row whose text contains
    /// <paramref name="reasonFragment"/>. When a request is rejected the Reason column
    /// switches to the rejection reason, so pass <paramref name="altFragment"/> as the
    /// rejection reason to find the row in that case.
    /// </summary>
    public async Task<string?> GetLeaveRequestStatusAsync(string reasonFragment, string? altFragment = null)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

        if (!await row.IsVisibleAsync() && altFragment is not null)
        {
            row = page.Locator("table tbody tr")
                .Filter(new() { HasText = altFragment })
                .First;
        }

        if (!await row.IsVisibleAsync()) return null;

        var statusCell = row.Locator(".badge");
        return (await statusCell.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns the rejection reason text for the leave request row matching
    /// <paramref name="reasonFragment"/>, taken from the Reason column.
    /// </summary>
    public async Task<string?> GetLeaveRequestRejectionReasonAsync(string reasonFragment)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

        // Reason is in td index 5 (Type, Start, End, Days, Status, Reason, action)
        var cells = await row.Locator("td").AllAsync();
        if (cells.Count < 6) return null;
        return (await cells[5].TextContentAsync())?.Trim();
    }

    // ── Request Leave dialog ───────────────────────────────────────────────────

    public async Task FillLeaveRequestAsync(
        string leaveTypeName,
        string startDate,   // dd/MM/yyyy
        string endDate,     // dd/MM/yyyy
        string? reason = null)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        // Syncfusion SfDropDownList renders a readonly <input> inside a <span role="combobox">.
        // The span intercepts all pointer events, so we must click the span, not the input.
        // Scope to the leave-type combobox via :has() on its input's unique placeholder.
        var typeCombobox = dialog.Locator("span[role='combobox']:has([placeholder='Select leave type'])");
        await typeCombobox.ClickAsync();
        // Wait for the specific list item to exist in the popup — not just the popup container,
        // because Syncfusion renders items asynchronously after the popup opens.
        var targetItem = page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = leaveTypeName })
            .First;
        await targetItem.WaitForAsync(new() { Timeout = 10_000 });
        await targetItem.ClickAsync();

        // Start date — fill into the first SfDatePicker input.
        var dateInputs = dialog.Locator(".e-date-wrapper input.e-input");
        await dateInputs.Nth(0).ClickAsync();
        await dateInputs.Nth(0).FillAsync(startDate);
        await page.Keyboard.PressAsync("Tab"); // commit the date

        // End date.
        await dateInputs.Nth(1).ClickAsync();
        await dateInputs.Nth(1).FillAsync(endDate);
        await page.Keyboard.PressAsync("Tab");

        // Optional reason.
        if (!string.IsNullOrEmpty(reason))
        {
            var reasonInput = dialog.GetByPlaceholder("Reason for leave request");
            await reasonInput.FillAsync(reason);
        }
    }

    public async Task SubmitLeaveRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        // Dialog closes on success; wait for it to disappear.
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }
}
