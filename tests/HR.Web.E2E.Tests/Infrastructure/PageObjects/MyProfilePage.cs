using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class MyProfilePage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile");
        await page.WaitForSelectorAsync(".e-tab, .tab-content, [role='tablist']",
            new() { Timeout = 20_000 });
    }

    // ── Leave tab ─────────────────────────────────────────────────────────────

    public async Task OpenLeaveTabAsync()
    {
        var leaveTab = page.GetByRole(AriaRole.Tab, new() { Name = "Leave" });
        await leaveTab.ClickAsync();
        // Wait for the leave balance card or the "No leave requests yet" message.
        await page.WaitForSelectorAsync(".card-header, p.text-muted", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Reads the Annual Leave remaining days from the balance card.
    /// Returns null if the card is not yet visible.
    /// </summary>
    public async Task<decimal?> GetAnnualLeaveRemainingAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd   = card.Locator("dd.fs-4.fw-semibold");
        if (!await dd.IsVisibleAsync()) return null;
        var text = (await dd.TextContentAsync())?.Trim() ?? "";
        // format is "25 days" or "20.5 days"
        var parts = text.Split(' ');
        return decimal.TryParse(parts[0], out var v) ? v : null;
    }

    /// <summary>Opens the Request Leave dialog.</summary>
    public async Task ClickRequestLeaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Leave" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-dialog", new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the status of the most recently submitted leave request row
    /// whose reason matches <paramref name="reasonFragment"/>.
    /// </summary>
    public async Task<string?> GetLeaveRequestStatusAsync(string reasonFragment)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

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
        var dialog = page.Locator(".e-dialog");

        // Leave type — click the SfDropDownList input, wait for the popup, pick the option.
        var typeInput = dialog.Locator(".e-dropdownlist input.e-input").First;
        await typeInput.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = leaveTypeName })
            .First
            .ClickAsync();

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
        await page.WaitForSelectorAsync(".e-dialog", new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }
}
