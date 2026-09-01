using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Story 2: page object for the customer-facing organisation data export panel
/// (OrganisationDataExportPanel.razor), embedded on the Subscription page (/subscription) since
/// HR.Web has no dedicated customer account-closure page. Company Administrator only (the
/// Subscription page itself is company-admin scoped).
///
/// Layout: a card headed "Export organisation data" with an explanatory paragraph, a "Request
/// export" primary button (disabled while an export is Pending/InProgress), a "Refresh" button, a
/// status &lt;dl&gt; ("Status" / "Requested" / when completed "Available until" + a "Download
/// export" button), and — when there is history — an "Export history" HrGrid.
/// </summary>
public sealed class OrganisationDataExportPanelPage(IPage page, string baseUrl)
{
    private ILocator Panel => page.Locator("section[aria-labelledby='org-data-export-heading']");

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/subscription");
        await Panel.Locator("h5", new() { HasText = "Export organisation data" })
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    public async Task<bool> IsVisibleAsync() => await Panel.IsVisibleAsync();

    public ILocator RequestButton =>
        Panel.GetByRole(AriaRole.Button, new() { Name = "Request a new organisation data export" });

    public ILocator RefreshButton =>
        Panel.GetByRole(AriaRole.Button, new() { Name = "Refresh export status" });

    public ILocator DownloadButton =>
        Panel.GetByRole(AriaRole.Button, new() { Name = "Download the completed organisation data export" });

    public async Task<bool> IsRequestDisabledAsync() => await RequestButton.IsDisabledAsync();

    public async Task ClickRequestAsync()
    {
        await RequestButton.ClickAsync();
        // The panel reloads latest status after POSTing; wait for the confirmation alert or a
        // disabled Request button (whichever the new state produces).
        await page.WaitForTimeoutAsync(1_000);
    }

    public async Task ClickRefreshAsync()
    {
        await RefreshButton.ClickAsync();
        await page.WaitForTimeoutAsync(1_000);
    }

    public async Task<string?> StatusTextAsync()
    {
        var dd = Panel.Locator("dl dd").First;
        return await dd.CountAsync() > 0 ? (await dd.TextContentAsync())?.Trim() : null;
    }

    public ILocator HistoryGrid => Panel.Locator(".e-grid");

    public async Task<int> HistoryRowCountAsync() =>
        await HistoryGrid.CountAsync() == 0 ? 0 : await HistoryGrid.Locator(".e-row").CountAsync();
}
