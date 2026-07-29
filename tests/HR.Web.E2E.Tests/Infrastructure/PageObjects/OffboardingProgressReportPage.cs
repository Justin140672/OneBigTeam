using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Offboarding Progress report
/// (/companies/{companyId}/reporting/offboarding-progress — OffboardingProgressReportPage.razor).
/// This page has no ReportFilterPanel or any filter control at all — just three summary stat
/// cards (Total Employees / Outstanding Access / Outstanding Assets) above the grid, and export
/// via the same SfDropDownButton pattern as the other report pages.
/// </summary>
public sealed class OffboardingProgressReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/offboarding-progress");
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

    // ── Summary stat cards ─────────────────────────────────────────────────────

    private ILocator StatCard(string labelText) =>
        page.Locator(".card").Filter(new() { HasText = labelText }).First;

    public async Task<int> GetStatValueAsync(string labelText)
    {
        var text = await StatCard(labelText).Locator(".fs-4").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
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
