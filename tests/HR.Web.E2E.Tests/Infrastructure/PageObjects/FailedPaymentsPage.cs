using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class FailedPaymentsPage(IPage page, string baseUrl)
{
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

    public async Task SearchAsync(string term)
    {
        await SearchBox.FillAsync(term);
        await page.WaitForTimeoutAsync(500);
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
