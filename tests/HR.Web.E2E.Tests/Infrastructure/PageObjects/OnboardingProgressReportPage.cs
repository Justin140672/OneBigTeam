using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Onboarding Progress report
/// (/companies/{companyId}/reporting/onboarding-progress — OnboardingProgressReportPage.razor).
/// Has a single "Overdue only" checkbox filter + "Apply" button (no ReportFilterPanel), three
/// summary stat cards (Total Employees / Total Outstanding Tasks / Overdue Employees) above the
/// grid, and export via the same SfDropDownButton pattern as the other report pages.
/// </summary>
public sealed class OnboardingProgressReportPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/onboarding-progress");
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

    // ── Filter ─────────────────────────────────────────────────────────────────

    private ILocator OverdueOnlyCheckbox => page.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "Overdue only" }).First;

    public async Task SetOverdueOnlyAsync(bool value)
    {
        var isChecked = await OverdueOnlyCheckbox.Locator("input[type='checkbox']").IsCheckedAsync();
        if (isChecked != value)
            await OverdueOnlyCheckbox.ClickAsync();
    }

    public async Task ApplyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
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
