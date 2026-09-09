using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's CustomerList.razor (/customers) — the platform-admin-only
/// Syncfusion grid of all tenant companies. Row selection navigates into the read-only
/// CustomerDetails.razor page at /customers/{CompanyId}.
/// </summary>
public sealed class CustomerListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .dashboard-error";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/customers");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Types <paramref name="term"/> into the grid's server-side search box and waits for the
    /// debounced re-query to settle. The grid pages at 20 rows (CustomerList.razor), and other
    /// E2E flows (sign-up, company onboarding) create real tenant companies during a run, so a
    /// specific seeded company (e.g. "Acme Corporation") is NOT guaranteed to be on the first
    /// rendered page. Searching first makes any HasCompany/ClickCompanyRow assertion deterministic
    /// regardless of how many other companies exist.
    /// </summary>
    public async Task SearchAsync(string term)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var searchInput = page.Locator(".customer-search-box input");
        await searchInput.FillAsync(term);
        // SfTextBox commits its value on the native change event (focus loss), not per keystroke,
        // then CustomerList.razor debounces the query by 300ms — see CustomerListAdminTests.
        await searchInput.PressAsync("Tab");
        await page.WaitForTimeoutAsync(600);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public async Task<bool> HasCompanyAsync(string companyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = companyNameFragment })
            .First
            .IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the grid row for the given company name and waits for the resulting navigation
    /// into /customers/{CompanyId} (RowSelected in CustomerList.razor navigates directly, no
    /// intermediate confirmation step).
    /// </summary>
    public async Task ClickCompanyRowAsync(string companyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = companyNameFragment }).First;
        await row.ClickAsync();
        await page.WaitForURLAsync(url => System.Text.RegularExpressions.Regex.IsMatch(
            url.ToString(), @"/customers/[0-9a-fA-F-]{36}$"), new() { Timeout = 15_000 });
    }
}
