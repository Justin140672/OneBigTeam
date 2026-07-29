using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Employee Starter report
/// (/companies/{companyId}/reporting/employee-starters — EmployeeStarterReportPage.razor).
/// </summary>
public sealed class EmployeeStarterReportPage(IPage page, string baseUrl)
{
    // Same reasoning as EmployeeDirectoryReportPage.RowsRenderedSelector — Syncfusion's EJ2 grid
    // populates ".e-row"/".e-rowcell" on a separate JS render pass after the Blazor component
    // mounts, so waiting for the row selector (or its empty-state sibling) is the only race-free
    // wait tied to data actually being present.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/employee-starters");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    // ── Filter panel (ReportFilterPanel — Department/Location/PositionProfile/EmploymentType/DateRange only) ──

    private ILocator FilterField(string labelText) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = labelText }).First;

    public async Task SelectFilterAsync(string labelText, string valueText)
    {
        await DropDownSelector.SelectAsync(page, FilterField(labelText), valueText);
    }

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    // ── Export ─────────────────────────────────────────────────────────────────

    public async Task<IDownload> ExportAsync(string formatLabel)
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Export" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = formatLabel }).ClickAsync();
        return await downloadTask;
    }

    /// <summary>True if the page rendered its own graceful error banner rather than crashing (e.g. on a 403 from the report data endpoint).</summary>
    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();
}
