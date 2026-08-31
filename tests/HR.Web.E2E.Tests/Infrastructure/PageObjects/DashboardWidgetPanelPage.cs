using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// DSH-03 — drives the recruitment-summary widget panel on the Recruitment Dashboard
/// (src/HR.Web/Components/Pages/Dashboards/RecruitmentDashboard.razor). That panel is the first
/// consumer of WidgetPanelState / WidgetSourceLoader / WidgetSourceWarning: successfully-loaded KPI
/// tiles keep rendering while any failed source shows an inline
/// <c>.widget-source-warning</c> row with a Retry control, and a genuine all-empty load shows the
/// "All clear" block instead.
/// </summary>
public sealed class DashboardWidgetPanelPage(IPage page, string baseUrl)
{
    private ILocator Panel => page.Locator("section.dashboard-section").Filter(new() { Has = page.Locator(".widget-card") }).First;

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/recruitment");
        await page.WaitForSelectorAsync(".recruitment-dashboard, .dashboard-heading", new() { Timeout = 20_000 });
    }

    /// <summary>Waits for the summary panel to finish its initial load (KPI row, warning, or all-clear visible).</summary>
    public async Task WaitForPanelLoadedAsync() =>
        await Panel.Locator(".widget-kpi-row, .widget-source-warning, .widget-all-clear").First
            .WaitForAsync(new() { Timeout = 20_000 });

    /// <summary>True once the successfully-loaded KPI tile row is rendered.</summary>
    public async Task<bool> HasKpiRowAsync() =>
        await Panel.Locator(".widget-kpi-row").IsVisibleAsync();

    /// <summary>The visible KPI tile labels, in DOM order.</summary>
    public async Task<IReadOnlyList<string>> KpiTileLabelsAsync()
    {
        var labels = await Panel.Locator(".widget-kpi-row .recruitment-summary-tile-label, .widget-kpi-row [role='listitem']").AllInnerTextsAsync();
        return labels.Select(l => l.Trim()).ToList();
    }

    /// <summary>Number of inline per-source failure warnings currently shown in the panel.</summary>
    public async Task<int> SourceWarningCountAsync() =>
        await Panel.Locator(".widget-source-warning").CountAsync();

    /// <summary>True if an inline warning naming <paramref name="sourceName"/> is visible.</summary>
    public async Task<bool> HasSourceWarningAsync(string sourceName) =>
        await Panel.Locator(".widget-source-warning").Filter(new() { HasText = sourceName }).First.IsVisibleAsync();

    /// <summary>Clicks the Retry button on the inline warning for <paramref name="sourceName"/>.</summary>
    public async Task RetrySourceAsync(string sourceName)
    {
        var warning = Panel.Locator(".widget-source-warning").Filter(new() { HasText = sourceName }).First;
        await warning.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = false }).ClickAsync();
    }

    /// <summary>Waits until no inline warning for <paramref name="sourceName"/> remains (successful retry).</summary>
    public async Task WaitForSourceWarningClearedAsync(string sourceName) =>
        await Panel.Locator(".widget-source-warning").Filter(new() { HasText = sourceName }).First
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 20_000 });

    /// <summary>True if the genuine "All clear" empty-state block is shown.</summary>
    public async Task<bool> IsAllClearAsync() =>
        await Panel.Locator(".widget-all-clear").IsVisibleAsync();
}
