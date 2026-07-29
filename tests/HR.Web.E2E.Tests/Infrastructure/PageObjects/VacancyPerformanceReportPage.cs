using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Vacancy Performance report
/// (/companies/{companyId}/reporting/vacancy-performance — VacancyPerformanceReportPage.razor).
/// Uses a date-range-only ReportFilterPanel (no group-by control on this page, unlike Sickness
/// and Recruitment Pipeline) and exports via the same SfDropDownButton pattern as the other
/// report pages.
/// </summary>
public sealed class VacancyPerformanceReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/vacancy-performance");
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

    // ── Filter panel (ReportFilterPanel — Date range only: "Start Date From"/"Start Date To") ──

    private ILocator FilterField(string labelText) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = labelText }).First;

    public async Task FillDateRangeStartAsync(string ddMMyyyy) =>
        await FilterField("Start Date From").Locator(".e-date-wrapper input.e-input").FillAsync(ddMMyyyy);

    public async Task FillDateRangeEndAsync(string ddMMyyyy) =>
        await FilterField("Start Date To").Locator(".e-date-wrapper input.e-input").FillAsync(ddMMyyyy);

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
