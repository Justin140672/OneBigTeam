using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the ADM-03 Administrative Alerts &amp; Incidents inbox
/// (/companies/{companyId}/administrative-alerts — AdministrativeAlertsInboxPage.razor).
///
/// The page gates on <c>AppSession.CanViewAdminAlerts</c> (HR Administrator only; permission
/// <c>admin-alerts:view</c>). Other personas are client-side redirected to /access-denied by
/// <c>AppSession.GuardAccess</c>.
///
/// Layout: three summary count cards (Unread / Open / Critical), a filters card with Syncfusion
/// SfDropDownList for Severity / Category / Status (each carrying an explicit "All ..." item), an
/// "Unread only" SfCheckBox, two SfDatePicker for the occurred-date range, and a "Clear" button —
/// every filter reloads on change, there is no Apply button. Below sits an <c>HrGrid</c> of alert
/// rows (Severity / Category / Summary / Occurrences / Last occurred / Status / Actions), or a
/// green "No administrative alerts." panel when the filtered result is empty. Row actions: "Open"
/// (relative actionUrl nav), "Acknowledge" (status == Open), "Resolve" (opens a dialog with an
/// optional multiline note + confirm; shown while status != Resolved), "Mark read" (while unread).
/// A dismissible warning banner appears on a 409 ("Alert already updated…").
///
/// Data-dependent assertions degrade gracefully when the shared seeded Acme company happens to
/// have no administrative alerts, matching how ComplianceCentreTests handles its possibly-empty
/// data.
/// </summary>
public sealed class AdministrativeAlertsInboxPage(IPage page, string baseUrl)
{
    // Once loading finishes the page shows either grid rows, an empty grid, the green
    // "no administrative alerts" panel, or the red load-error alert.
    private const string LoadedSelector =
        ".e-grid .e-row, .e-grid .e-emptyrow, .alert-success, .alert-danger";

    // Grid column order (see AdministrativeAlertsInboxPage.razor GridColumns).
    private const int StatusCellIndex = 5;

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/administrative-alerts");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public string Url => page.Url;

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();

    // ── Summary count cards ────────────────────────────────────────────────────

    private ILocator SummaryRow => page.Locator("div.row.g-3.mb-3").First;

    private ILocator SummaryCard(string label) =>
        SummaryRow.Locator(".card").Filter(new() { HasText = label }).First;

    public async Task<int> SummaryValueAsync(string label)
    {
        var text = await SummaryCard(label).Locator(".fs-4").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
    }

    public Task<int> UnreadCountAsync() => SummaryValueAsync("Unread");

    // ── Filters ────────────────────────────────────────────────────────────────

    private ILocator FilterField(string label) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = label }).First;

    public async Task FilterBySeverityAsync(string label)
    {
        await DropDownSelector.SelectAsync(page, FilterField("Severity"), label);
        await WaitForReloadAsync();
    }

    public async Task FilterByCategoryAsync(string label)
    {
        await DropDownSelector.SelectAsync(page, FilterField("Category"), label);
        await WaitForReloadAsync();
    }

    public async Task FilterByStatusAsync(string label)
    {
        await DropDownSelector.SelectAsync(page, FilterField("Status"), label);
        await WaitForReloadAsync();
    }

    public async Task SetUnreadOnlyAsync(bool on)
    {
        var checkbox = page.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "Unread only" }).First;
        var isChecked = await checkbox.Locator(".e-check").CountAsync() > 0;
        if (isChecked != on)
        {
            await checkbox.Locator(".e-frame").ClickAsync();
            await WaitForReloadAsync();
        }
    }

    /// <summary>Resets every filter via the "Clear" button (the dropdowns also carry "All ..." items).</summary>
    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await WaitForReloadAsync();
    }

    private async Task WaitForReloadAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(400);
    }

    // ── Alert grid ─────────────────────────────────────────────────────────────

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.Locator(".alert-success", new() { HasText = "No administrative alerts" })
            .IsVisibleAsync();

    public async Task<int> AlertCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        if (await IsEmptyStateVisibleAsync())
            return 0;
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    public async Task<bool> HasAlertWithSummaryAsync(string summaryFragment)
    {
        if (await AlertCountAsync() == 0)
            return false;
        return await page.Locator(".e-grid .e-rowcell")
            .Filter(new() { HasText = summaryFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    private ILocator Row(int index) => page.Locator(".e-grid .e-row").Nth(index);

    public async Task<string> RowStatusAsync(int index)
    {
        var cell = Row(index).Locator(".e-rowcell").Nth(StatusCellIndex);
        return (await cell.TextContentAsync())?.Trim() ?? "";
    }

    public async Task<int> IndexOfFirstRowWithButtonAsync(string buttonName)
    {
        var count = await AlertCountAsync();
        for (var i = 0; i < count; i++)
        {
            if (await Row(i).GetByRole(AriaRole.Button, new() { Name = buttonName }).CountAsync() > 0)
                return i;
        }
        return -1;
    }

    public async Task<bool> RowHasButtonAsync(int index, string buttonName) =>
        await Row(index).GetByRole(AriaRole.Button, new() { Name = buttonName }).CountAsync() > 0;

    /// <summary>Acknowledges the first Open alert. Returns its row index, or -1 when none is actionable.</summary>
    public async Task<int> AcknowledgeFirstAsync()
    {
        var index = await IndexOfFirstRowWithButtonAsync("Acknowledge");
        if (index < 0)
            return -1;
        await Row(index).GetByRole(AriaRole.Button, new() { Name = "Acknowledge" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await WaitForReloadAsync();
        return index;
    }

    /// <summary>
    /// Resolves the first non-resolved alert via the dialog, optionally recording a note.
    /// Returns its row index, or -1 when none is actionable.
    /// </summary>
    public async Task<int> ResolveFirstAsync(string? note)
    {
        var index = await IndexOfFirstRowWithButtonAsync("Resolve");
        if (index < 0)
            return -1;

        await Row(index).GetByRole(AriaRole.Button, new() { Name = "Resolve" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Resolve alert" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        if (!string.IsNullOrEmpty(note))
            await dialog.Locator("textarea").First.FillAsync(note);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Resolve" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await WaitForReloadAsync();
        return index;
    }

    /// <summary>Marks the first unread alert read. Returns its row index, or -1 when none is actionable.</summary>
    public async Task<int> MarkFirstReadAsync()
    {
        var index = await IndexOfFirstRowWithButtonAsync("Mark read");
        if (index < 0)
            return -1;
        await Row(index).GetByRole(AriaRole.Button, new() { Name = "Mark read" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await WaitForReloadAsync();
        return index;
    }
}
