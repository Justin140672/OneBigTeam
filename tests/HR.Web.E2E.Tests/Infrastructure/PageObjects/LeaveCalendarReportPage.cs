using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Leave Calendar report
/// (/companies/{companyId}/reporting/leave-calendar — LeaveCalendarReportPage.razor). This
/// report is export-oriented per its ticket, so its Export SfDropDownButton uses "e-primary"
/// styling rather than the "e-flat" style used by the other report pages.
/// </summary>
public sealed class LeaveCalendarReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/leave-calendar");
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

    // ── Inline filters ─────────────────────────────────────────────────────────

    private ILocator YearInput => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = "Year" }).First.Locator("input");

    public async Task SetYearAsync(int year)
    {
        await YearInput.FillAsync(year.ToString());
        await YearInput.PressAsync("Tab");
    }

    private ILocator MonthField => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = "Month" }).First;

    public async Task SelectMonthAsync(string monthLabel) =>
        await DropDownSelector.SelectAsync(page, MonthField, monthLabel);

    private ILocator DepartmentField => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = "Department" }).First;

    public async Task SelectDepartmentAsync(string departmentName) =>
        await DropDownSelector.SelectAsync(page, DepartmentField, departmentName);

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }).ClickAsync();
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
