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
