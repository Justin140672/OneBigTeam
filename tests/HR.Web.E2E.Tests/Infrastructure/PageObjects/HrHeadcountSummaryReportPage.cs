using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the HR Headcount Summary report
/// (/companies/{companyId}/reporting/hr-headcount-summary — HrHeadcountSummaryReportPage.razor).
/// Shows 5 summary stat cards (Total Headcount / Active Employees / Future Starters / Leavers /
/// Total FTE) above a ReportFilterPanel (Department/Location/EmploymentType/Status — no
/// PositionProfile or Manager filter) and a grid that supports drag-to-group (AllowGrouping="true").
/// </summary>
public sealed class HrHeadcountSummaryReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/hr-headcount-summary");
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

    // ── Summary stat cards ───────────────────────────────────────────────────────

    private ILocator StatCard(string label) =>
        page.Locator(".card").Filter(new() { HasText = label }).First;

    private async Task<int> GetStatIntAsync(string label)
    {
        var text = await StatCard(label).Locator(".fs-4").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
    }

    public Task<int> GetTotalHeadcountAsync() => GetStatIntAsync("Total Headcount");
    public Task<int> GetActiveEmployeesAsync() => GetStatIntAsync("Active Employees");
    public Task<int> GetFutureStartersAsync() => GetStatIntAsync("Future Starters");
    public Task<int> GetLeaversAsync() => GetStatIntAsync("Leavers");

    public async Task<decimal> GetTotalFteAsync()
    {
        var text = await StatCard("Total FTE").Locator(".fs-4").TextContentAsync();
        return decimal.TryParse(text?.Trim(), out var value) ? value : -1m;
    }

    // ── Filter panel (ReportFilterPanel — Department/Location/EmploymentType/Status only) ──

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

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();
}
