using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class OperationalAlertsPage(IPage page, string baseUrl)
{
    private const string SettledSelector =
        ".e-grid .e-row, .e-grid .e-emptyrow, .activity-empty, .dashboard-error";

    private string _companyFilter = "";
    private string _categoryFilter = "";
    private string _statusFilter = "open";
    private int _pageNumber = 1;

    private ILocator Results => page.Locator("[data-loaded-filter]");

    private async Task WaitForFilterAppliedAsync()
    {
        var key = $"{_companyFilter}|{_categoryFilter}|{_statusFilter}|{_pageNumber}";
        await Assertions.Expect(Results).ToHaveAttributeAsync("data-loaded-filter", key, new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
    }

    public async Task GotoAsync()
    {
        _companyFilter = "";
        _categoryFilter = "";
        _statusFilter = "open";
        _pageNumber = 1;
        await page.GotoAsync($"{baseUrl}/operational-alerts");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 30_000 });
        await WaitForFilterAppliedAsync();
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".activity-empty").IsVisibleAsync();

    public Task<bool> IsGridVisibleAsync() =>
        page.Locator(".e-grid").IsVisibleAsync();

    public async Task<bool> HasColumnHeaderAsync(string headerText)
    {
        await page.WaitForSelectorAsync(".e-grid .e-headercell", new() { Timeout = 15_000 });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var headers = await page.Locator(".e-grid .e-headercell .e-headertext").AllTextContentsAsync();
            if (headers.Any(h => string.Equals(h.Trim(), headerText, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (DateTime.UtcNow > deadline)
                return false;
            await Task.Delay(250);
        }
    }

    private ILocator CompanyIdInput => page.Locator(".customer-search-box input");
    private ILocator CategorySelect => page.Locator("select.failed-payments-status-filter").Nth(0);
    private ILocator StatusSelect => page.Locator("select.failed-payments-status-filter").Nth(1);

    public async Task SetCompanyIdFilterAsync(Guid companyId)
    {
        await CompanyIdInput.FillAsync(companyId.ToString());
        await CompanyIdInput.PressAsync("Tab");
        _companyFilter = companyId.ToString();
        _pageNumber = 1;
        await WaitForFilterAppliedAsync();
    }

    public async Task SetCategoryFilterAsync(string value)
    {
        await CategorySelect.SelectOptionAsync(new SelectOptionValue { Value = value });
        _categoryFilter = value;
        _pageNumber = 1;
        await WaitForFilterAppliedAsync();
    }

    public async Task SetStatusFilterAsync(string value)
    {
        await StatusSelect.SelectOptionAsync(new SelectOptionValue { Value = value });
        _statusFilter = value;
        _pageNumber = 1;
        await WaitForFilterAppliedAsync();
    }

    public async Task<int> RowCountAsync()
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        if (await page.Locator(".activity-empty").IsVisibleAsync())
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    public async Task<IReadOnlyList<string>> ColumnValuesAsync(string headerText)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        var headers = await page.Locator(".e-grid .e-headercell .e-headertext").AllTextContentsAsync();
        var index = -1;
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), headerText, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return Array.Empty<string>();

        var rows = page.Locator(".e-grid .e-row");
        var count = await rows.CountAsync();
        var values = new List<string>(count);
        for (var r = 0; r < count; r++)
        {
            var cell = rows.Nth(r).Locator(".e-rowcell").Nth(index);
            values.Add(((await cell.TextContentAsync()) ?? string.Empty).Trim());
        }

        return values;
    }

    public async Task OpenFirstRowAsync()
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 15_000 });
        await page.Locator(".e-grid .e-row").First.ClickAsync();
        await page.WaitForURLAsync(url => System.Text.RegularExpressions.Regex.IsMatch(
            url.ToString(), @"/operational-alerts/[0-9a-fA-F-]{36}$"), new() { Timeout = 15_000 });
    }

    public async Task OpenAlertAsync(Guid id)
    {
        await page.GotoAsync($"{baseUrl}/operational-alerts/{id}");
        await page.WaitForSelectorAsync(".details-panel, .dashboard-error", new() { Timeout = 20_000 });
    }

    private ILocator PagerButton(string name) =>
        page.Locator(".operational-alerts-pager").GetByRole(AriaRole.Button, new() { Name = name });

    public async Task NextPageAsync()
    {
        await PagerButton("Next").ClickAsync();
        _pageNumber++;
        await WaitForFilterAppliedAsync();
    }

    public async Task PreviousPageAsync()
    {
        await PagerButton("Previous").ClickAsync();
        _pageNumber--;
        await WaitForFilterAppliedAsync();
    }

    public Task<bool> IsNextEnabledAsync() => PagerButton("Next").IsEnabledAsync();
    public Task<bool> IsPreviousEnabledAsync() => PagerButton("Previous").IsEnabledAsync();
}
