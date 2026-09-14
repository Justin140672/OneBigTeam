using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's SupportRequestQueue.razor
/// (/customers/{CompanyId}/support-requests) — the Admin Portal's staff-only grid of a single
/// company's support requests, reached via CustomerDetailsPage's "Open support requests" link.
/// Row selection navigates to AdminSupportRequestDetailPage.
/// </summary>
public sealed class AdminSupportRequestQueuePage(IPage page, string baseUrl)
{
    // Renders exactly one of: "Loading…", the "not authorised" dashboard-error div, the
    // "No support requests found" empty state, or the populated grid.
    private const string ResolvedSelector = ".hr-grid, .dashboard-error, .activity-empty";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/customers/{companyId}/support-requests");
        await page.WaitForSelectorAsync(ResolvedSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<bool> IsGridVisibleAsync() =>
        page.Locator(".hr-grid").IsVisibleAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".activity-empty").IsVisibleAsync();

    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .dashboard-error, .activity-empty";

    public async Task<bool> HasRequestAsync(string referenceOrTitleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = referenceOrTitleFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    /// <summary>
    /// Clicks the grid row matching <paramref name="referenceOrTitleFragment"/> (SfGrid's
    /// RowSelected event navigates to the detail page — see SupportRequestQueue.razor's
    /// OnRowSelected) and waits for the resulting navigation to land on a support-requests/{id}
    /// detail URL.
    /// </summary>
    public async Task OpenRequestAsync(string referenceOrTitleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = referenceOrTitleFragment }).First;
        await row.ClickAsync();
        await page.WaitForURLAsync(
            url => System.Text.RegularExpressions.Regex.IsMatch(url.ToString(), "/support-requests/[0-9a-fA-F-]{36}$"),
            new() { Timeout = 20_000 });
    }

    public ILocator BackToCustomerDetailsLink =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to customer details" });
}
