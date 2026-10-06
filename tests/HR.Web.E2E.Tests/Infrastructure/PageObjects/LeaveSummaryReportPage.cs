using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class LeaveSummaryReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/leave-summary");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<string> result;
        do
        {
            await page.WaitForSelectorAsync(".e-headercell", new() { Timeout = 15_000 });
            var headers = await page.Locator(".e-headercell").AllAsync();
            result = new List<string>();
            foreach (var header in headers)
                result.Add((await header.TextContentAsync())?.Trim() ?? "");

            if (result.Any(h => !string.IsNullOrWhiteSpace(h)))
                return result;

            await page.WaitForTimeoutAsync(200);
        } while (DateTime.UtcNow < deadline);

        return result;
    }

    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }


    private ILocator PolicyYearInput => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = "Policy Year" }).First.Locator("input");

    public async Task SetPolicyYearAsync(int year)
    {
        await PolicyYearInput.FillAsync(year.ToString());
        await PolicyYearInput.PressAsync("Tab");
    }

    private ILocator DepartmentField => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = "Department" }).First;

    public async Task SelectDepartmentAsync(string departmentName) =>
        await DropDownSelector.SelectAsync(page, DepartmentField, departmentName);

    private ILocator GroupByField => page.Locator(".card-body .col-md-3")
        .Filter(new() { HasText = "Group By" }).First;

    public async Task SelectGroupByAsync(string groupByLabel)
    {
        await DropDownSelector.SelectAsync(page, GroupByField, groupByLabel);
        await ApplyFiltersAsync();
    }

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }


    public Task<IDownload> ExportAsync(string formatLabel) =>
        ReportExport.ExportAsync(page, formatLabel);

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();
}
