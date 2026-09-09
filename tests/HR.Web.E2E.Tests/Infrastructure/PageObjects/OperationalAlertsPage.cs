using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's OperationalAlerts.razor (/operational-alerts) — the
/// platform-admin-only list of system-generated operational alerts (Follow-up B). A debounced
/// company-id <c>SfTextBox</c>, two native <c>&lt;select&gt;</c> filters (category, then status —
/// status defaults to "open"), a Syncfusion grid, and a server-side Previous/Next pager. Row click
/// navigates to /operational-alerts/{id}.
///
/// The page renders exactly one of: "Loading…" text, the ".dashboard-error" not-authorised banner,
/// the ".activity-empty" no-results paragraph, or the grid — <see cref="SettledSelector"/> waits for
/// any settled state. Both filter <c>&lt;select&gt;</c> elements share the CSS class
/// "failed-payments-status-filter" (reused from FailedPayments.razor's toolbar), so they're
/// addressed positionally: category is index 0, status is index 1.
/// </summary>
public sealed class OperationalAlertsPage(IPage page, string baseUrl)
{
    private const string SettledSelector =
        ".e-grid .e-row, .e-grid .e-emptyrow, .activity-empty, .dashboard-error";

    public async Task GotoAsync()
    {
        await page.GotoAsync($"{baseUrl}/operational-alerts");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
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
        var headers = await page.Locator(".e-grid .e-headercell .e-headertext").AllTextContentsAsync();
        return headers.Any(h => string.Equals(h.Trim(), headerText, StringComparison.OrdinalIgnoreCase));
    }

    private ILocator CompanyIdInput => page.Locator(".customer-search-box input");
    private ILocator CategorySelect => page.Locator("select.failed-payments-status-filter").Nth(0);
    private ILocator StatusSelect => page.Locator("select.failed-payments-status-filter").Nth(1);

    /// <summary>
    /// Types into the debounced (300ms) SfTextBox company-id filter. SfTextBox commits its bound
    /// value on the native change event (focus loss), not per keystroke — Tab out, then wait past
    /// the debounce window so the reload has actually been triggered.
    /// </summary>
    public async Task SetCompanyIdFilterAsync(Guid companyId)
    {
        await CompanyIdInput.FillAsync(companyId.ToString());
        await CompanyIdInput.PressAsync("Tab");
        await page.WaitForTimeoutAsync(600);
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(200);
    }

    public async Task SetCategoryFilterAsync(string value)
    {
        await CategorySelect.SelectOptionAsync(new SelectOptionValue { Value = value });
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(200);
    }

    public async Task SetStatusFilterAsync(string value)
    {
        await StatusSelect.SelectOptionAsync(new SelectOptionValue { Value = value });
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(200);
    }

    public async Task<int> RowCountAsync()
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        if (await page.Locator(".activity-empty").IsVisibleAsync())
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    /// <summary>Text of every rendered cell under the given grid column header.</summary>
    public async Task<IReadOnlyList<string>> ColumnValuesAsync(string headerText)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        // Resolve the column's 0-based index from its header, then read that <td> per row.
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
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(200);
    }

    public async Task PreviousPageAsync()
    {
        await PagerButton("Previous").ClickAsync();
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(200);
    }

    public Task<bool> IsNextEnabledAsync() => PagerButton("Next").IsEnabledAsync();
    public Task<bool> IsPreviousEnabledAsync() => PagerButton("Previous").IsEnabledAsync();
}
