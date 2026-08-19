using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Recruitment Pipeline Summary report
/// (/companies/{companyId}/reporting/recruitment-pipeline-summary —
/// RecruitmentPipelineSummaryReportPage.razor). Shows a grid of vacancies with a per-stage
/// candidate-count "Pipeline Stages" badge column, plus a standalone "Include closed vacancies"
/// SfCheckBox (outside the ReportFilterPanel — this page has no ReportFilterPanel at all) and the
/// same SfDropDownButton export pattern as the other report pages.
/// </summary>
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

    /// <summary>
    /// Text of every "Pipeline Stages" badge (e.g. "Screening: 2") rendered across all grid rows —
    /// used to assert the per-stage candidate counts actually show up for a vacancy with candidates.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetPipelineStageBadgeTextsAsync()
    {
        var badges = await page.Locator(".e-grid .e-row .badge.bg-secondary").AllAsync();
        var result = new List<string>();
        foreach (var badge in badges)
            result.Add((await badge.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    // ── "Include closed vacancies" checkbox (standalone SfCheckBox, no filter panel/Apply button —
    // OnIncludeClosedChangedAsync reloads the grid immediately on toggle) ──

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
