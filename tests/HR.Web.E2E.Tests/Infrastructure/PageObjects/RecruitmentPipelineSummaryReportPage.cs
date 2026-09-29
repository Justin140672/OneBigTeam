using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class RecruitmentPipelineSummaryReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/recruitment-pipeline-summary");
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

    public async Task<IReadOnlyList<string>> GetPipelineStageBadgeTextsAsync()
    {
        var badges = await page.Locator(".e-grid .e-row .badge.bg-secondary").AllAsync();
        var result = new List<string>();
        foreach (var badge in badges)
            result.Add((await badge.TextContentAsync())?.Trim() ?? "");
        return result;
    }


    private ILocator IncludeClosedCheckbox =>
        page.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "Include closed vacancies" }).First;

    public async Task<bool> IsIncludeClosedCheckedAsync() =>
        await IncludeClosedCheckbox.Locator("input[type='checkbox']").IsCheckedAsync();

    public async Task SetIncludeClosedAsync(bool value)
    {
        var isChecked = await IsIncludeClosedCheckedAsync();
        if (isChecked == value) return;

        await IncludeClosedCheckbox.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(300);
    }


    public async Task<IDownload> ExportAsync(string formatLabel)
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Export" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = formatLabel }).ClickAsync();
        return await downloadTask;
    }

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();

    // ── Applications type filter (internal recruitment Ticket 6) ──────────────

    public Task SelectApplicationTypeAsync(string label) => ReportApplicationTypeFilter.SelectAsync(page, label);

    public Task ExpectApplicationTypeAsync(string label) => ReportApplicationTypeFilter.ExpectSelectedAsync(page, label);

    public Task ExpectRenderedWithoutErrorAsync() => ReportApplicationTypeFilter.ExpectGridRenderedWithoutErrorAsync(page);
}
