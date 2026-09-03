using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's CustomerSupportView.razor (/customers/{CompanyId}/support) — a
/// fully read-only, troubleshooting-optimised summary of one tenant (subscription/trial, users vs.
/// employees, recent billing snapshots, platform-wide background job health, and several honest
/// "not yet available" panels). There is no create/edit/delete affordance on this page.
///
/// Renders exactly one of: "Loading…", the "not authorised / couldn't be found" dashboard-error
/// div, or the details grid — GoToAsync waits for whichever settles first.
/// </summary>
public sealed class CustomerSupportViewPage(IPage page, string baseUrl)
{
    private const string ResolvedSelector = ".details-grid, .dashboard-error";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/customers/{companyId}/support");
        await page.WaitForSelectorAsync(ResolvedSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<string?> GetErrorBannerTextAsync() =>
        page.Locator(".dashboard-error").TextContentAsync();

    public Task<bool> IsDetailsGridVisibleAsync() =>
        page.Locator(".details-grid").IsVisibleAsync();

    private ILocator GetSection(string headingText) =>
        page.Locator("section.details-panel")
            .Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = headingText, Exact = true }) });

    public async Task<string?> GetKeyValueAsync(string headingText, string key)
    {
        var value = GetSection(headingText)
            .Locator($"xpath=.//dt[normalize-space(text())='{key}']/following-sibling::dd[1]");
        return (await value.TextContentAsync())?.Trim();
    }

    public Task<string?> GetCompanyNameAsync() => GetKeyValueAsync("Subscription & trial", "Company");

    public Task<string?> GetSubscriptionStatusAsync() => GetKeyValueAsync("Subscription & trial", "Subscription status");

    public async Task<string?> GetStatCardValueAsync(string label)
    {
        var card = page.Locator(".stat-cards .stat-card").Filter(new() { HasText = label });
        return (await card.Locator(".stat-value").TextContentAsync())?.Trim();
    }

    public ILocator BackToCustomerDetailsLink =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to customer details" });

    public Task<bool> IsRecentInvoicesEmptyStateVisibleAsync() =>
        GetSection("Recent invoices").GetByText("No billing snapshots recorded yet.").IsVisibleAsync();

    public Task<bool> IsRecentInvoicesTableVisibleAsync() =>
        GetSection("Recent invoices").Locator("table.billing-history-table").IsVisibleAsync();
}
