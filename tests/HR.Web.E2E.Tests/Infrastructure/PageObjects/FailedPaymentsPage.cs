using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's FailedPayments.razor (/failed-payments) — the platform-admin-only
/// read-only dashboard listing tenants with failed/uncollectible Stripe invoices. Unlike
/// CustomerList/CustomerDetails, there is no create/edit/delete flow here — just search, a plain
/// native &lt;select&gt; status filter, and row-click navigation into /customers/{CompanyId}.
///
/// In this dev/test environment Stripe is not configured (no live key), so
/// FailedPaymentsResponse.StripeConfigured is expected to be false and the page renders the
/// "Stripe is not configured" dashboard-error state rather than the grid — see FailedPayments.razor.
/// </summary>
public sealed class FailedPaymentsPage(IPage page, string baseUrl)
{
    // FailedPayments.razor renders exactly one of: loading text, the "not authorised"/"not
    // configured" dashboard-error div, the empty-results paragraph, or the grid — wait for any of
    // the "settled" states.
    private const string SettledSelector = ".dashboard-error, .activity-empty, .hr-grid, .e-grid";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/failed-payments");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public ILocator SearchBox => page.Locator(".customer-search-box input, input.customer-search-box");

    public ILocator StatusFilterSelect => page.Locator("select.failed-payments-status-filter");

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<string?> GetErrorBannerTextAsync() =>
        page.Locator(".dashboard-error").TextContentAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".activity-empty").IsVisibleAsync();

    public Task<string?> GetEmptyStateTextAsync() =>
        page.Locator(".activity-empty").TextContentAsync();

    public Task<bool> IsGridVisibleAsync() =>
        page.Locator(".hr-grid").IsVisibleAsync();

    /// <summary>
    /// Types into the debounced (300ms) search box and waits past the debounce window so the
    /// resulting reload has actually been triggered before the caller asserts anything.
    /// </summary>
    public async Task SearchAsync(string term)
    {
        await SearchBox.FillAsync(term);
        await page.WaitForTimeoutAsync(500);
        // SettledSelector matches any of several possible end states (error banner, empty state,
        // or a real grid) — a caller re-searching/re-filtering can have this resolve against a
        // transient loading-state grid container that appears briefly before the page settles
        // into its true final state (e.g. the Stripe-not-configured error banner), same "resolves
        // against stale/transient content" race documented elsewhere in this suite. A short settle
        // after the match gives the real final state a moment to land.
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task SelectStatusFilterAsync(string value)
    {
        await StatusFilterSelect.SelectOptionAsync(new SelectOptionValue { Value = value });
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<bool> HasCompanyAsync(string companyNameFragment)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = companyNameFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task ClickCompanyRowAsync(string companyNameFragment)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = companyNameFragment }).First;
        await row.ClickAsync();
        await page.WaitForURLAsync(url => System.Text.RegularExpressions.Regex.IsMatch(
            url.ToString(), @"/customers/[0-9a-fA-F-]{36}$"), new() { Timeout = 15_000 });
    }
}
