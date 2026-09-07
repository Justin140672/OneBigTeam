using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for SupportRequestQueue.razor (/companies/{companyId}/support/admin/queue) —
/// the staff-only grid of support requests. Status is a plain read-only, humanized text cell;
/// per the page's own banner ("Ticket status can only be changed by support staff in the Admin
/// app."), there is no per-row status control here any more.
/// </summary>
public sealed class SupportRequestQueuePage(IPage page, string baseUrl)
{
    // Mirrors EmploymentTypeListPage's RowsRenderedSelector reasoning: the Syncfusion grid
    // populates .e-row/.e-rowcell asynchronously after the Blazor component mounts.
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

    /// <summary>
    /// True if the row matching <paramref name="referenceOrTitleFragment"/> shows
    /// <paramref name="statusText"/> as its Status cell's plain text (see
    /// SupportRequestQueue.razor's Status column Template — EnumDisplay.Humanize(row.Status),
    /// no dropdown).
    /// </summary>
    public async Task<bool> HasStatusTextAsync(string referenceOrTitleFragment, string statusText)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = referenceOrTitleFragment }).First;
        return await row.Locator(".e-rowcell").Filter(new() { HasText = statusText }).First.WaitUntilVisibleAsync();
    }

    /// <summary>
    /// True if the row matching <paramref name="referenceOrTitleFragment"/> still has an
    /// editable status dropdown (span[role='combobox']) — expected to always be false now that
    /// status can only be changed by support staff in the Admin app.
    /// </summary>
    public async Task<bool> HasStatusDropdownAsync(string referenceOrTitleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row").Filter(new() { HasText = referenceOrTitleFragment }).First;
        return await row.Locator("span[role='combobox']").First.IsVisibleAsync();
    }

    public Task<bool> HasActionErrorAsync() =>
        page.Locator(".alert-danger").First.IsVisibleAsync();
}
