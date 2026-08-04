using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for SupportDashboard.razor (/companies/{companyId}/support/admin/dashboard) —
/// staff-only aggregated support/feedback metrics.
/// </summary>
public sealed class SupportDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/support/admin/dashboard");
        await page.WaitForSelectorAsync(".support-metric-card, .alert-danger", new() { Timeout = 20_000 });
    }

    public Task<bool> IsOpenRequestsCardVisibleAsync() =>
        page.Locator(".support-metric-card").Filter(new() { HasText = "Open Requests" }).IsVisibleAsync();

    public Task<bool> IsAverageResponseTimeCardVisibleAsync() =>
        page.Locator(".support-metric-card").Filter(new() { HasText = "Avg. Staff Response Time" }).IsVisibleAsync();

    public Task<bool> IsTopRequestedFeaturesCardVisibleAsync() =>
        page.Locator(".card").Filter(new() { HasText = "Top Requested Features" }).IsVisibleAsync();

    public Task<bool> IsTopReportedProblemsCardVisibleAsync() =>
        page.Locator(".card").Filter(new() { HasText = "Top Reported Problems" }).IsVisibleAsync();

    public Task<bool> IsRequestsByTypeCardVisibleAsync() =>
        page.Locator(".card").Filter(new() { HasText = "Requests by Type" }).IsVisibleAsync();

    public Task<bool> HasLoadErrorAsync() =>
        page.Locator(".alert-danger").IsVisibleAsync();
}
