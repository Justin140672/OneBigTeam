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

    public async Task WaitForPanelLoadedAsync() =>
        await Panel.Locator(".widget-kpi-row, .widget-source-warning, .widget-all-clear").First
            .WaitForAsync(new() { Timeout = 20_000 });

    public async Task<bool> HasKpiRowAsync() =>
        await Panel.Locator(".widget-kpi-row").IsVisibleAsync();

    public async Task<IReadOnlyList<string>> KpiTileLabelsAsync()
    {
        var labels = await Panel.Locator(".widget-kpi-row .recruitment-summary-tile-label, .widget-kpi-row [role='listitem']").AllInnerTextsAsync();
        return labels.Select(l => l.Trim()).ToList();
    }

    public async Task<int> SourceWarningCountAsync() =>
        await Panel.Locator(".widget-source-warning").CountAsync();

    public async Task<bool> HasSourceWarningAsync(string sourceName) =>
        await Panel.Locator(".widget-source-warning").Filter(new() { HasText = sourceName }).First.IsVisibleAsync();

    public async Task RetrySourceAsync(string sourceName)
    {
        var warning = Panel.Locator(".widget-source-warning").Filter(new() { HasText = sourceName }).First;
        await warning.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = false }).ClickAsync();
    }

    public async Task WaitForSourceWarningClearedAsync(string sourceName) =>
        await Panel.Locator(".widget-source-warning").Filter(new() { HasText = sourceName }).First
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 20_000 });

    public async Task<bool> IsAllClearAsync() =>
        await Panel.Locator(".widget-all-clear").IsVisibleAsync();
}
