using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SupportRequestQueuePage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/support/admin/queue");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task<bool> HasRequestAsync(string referenceOrTitleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = referenceOrTitleFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public Task SelectStatusFilterAsync(string status) =>
        DropDownSelector.SelectAsync(page, page.Locator(".support-status-filter"), status);

    public async Task<bool> HasStatusTextAsync(string referenceOrTitleFragment, string statusText)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = referenceOrTitleFragment }).First;
        return await row.Locator(".e-rowcell").Filter(new() { HasText = statusText }).First.WaitUntilVisibleAsync();
    }

    public async Task<bool> HasStatusDropdownAsync(string referenceOrTitleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = referenceOrTitleFragment }).First;
        return await row.Locator("span[role='combobox']").First.IsVisibleAsync();
    }

    public Task<bool> HasActionErrorAsync() =>
        page.Locator(".alert-danger").First.IsVisibleAsync();
}
