using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

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
        var outcome = Panel.Locator(".alert-success, .alert-danger").First;
        await outcome.WaitForAsync(new() { Timeout = 30_000 });
        var cls = await outcome.GetAttributeAsync("class") ?? string.Empty;
        if (cls.Contains("alert-danger"))
            throw new InvalidOperationException($"Export request failed: {(await outcome.TextContentAsync())?.Trim()}");
        await Panel.Locator("dl.row").WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickRefreshAsync()
    {
        await RefreshButton.ClickAsync();
        await WaitForReloadAsync();
    }

    private async Task WaitForReloadAsync()
    {
        await Panel.Locator("dl.row")
            .Or(Panel.GetByText("No export has been requested yet."))
            .First
            .WaitForAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(RefreshButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
    }

    public async Task<string?> StatusTextAsync()
    {
        var dd = Panel.Locator("dl dd").First;
        try
        {
            await dd.WaitForAsync(new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }

        return (await dd.TextContentAsync())?.Trim();
    }

    public ILocator HistoryGrid => Panel.Locator(".e-grid");

    public async Task<int> HistoryRowCountAsync() =>
        await HistoryGrid.CountAsync() == 0 ? 0 : await HistoryGrid.Locator(".e-row").CountAsync();
}
