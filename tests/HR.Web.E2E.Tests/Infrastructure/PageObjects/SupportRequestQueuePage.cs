using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for SupportRequestQueue.razor (/companies/{companyId}/support/admin/queue) —
/// the staff-only grid of support requests with a per-row required status dropdown.
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
            .IsVisibleAsync();
    }

    public Task SelectStatusFilterAsync(string status) =>
        DropDownSelector.SelectAsync(page, page.Locator(".support-status-filter"), status);

    /// <summary>
    /// Changes the status dropdown embedded in the row matching <paramref name="referenceOrTitleFragment"/>.
    /// The row's own dropdown is scoped via the row locator so this works regardless of position.
    /// </summary>
    public async Task ChangeStatusAsync(string referenceOrTitleFragment, string newStatus)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = referenceOrTitleFragment })
            .First;
        await DropDownSelector.SelectAsync(page, row, newStatus);
        // ChangeStatusAsync in the razor page reloads the grid on success; wait for the
        // spinner (if any) used elsewhere in this suite to clear before asserting further.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public Task<bool> HasActionErrorAsync() =>
        page.Locator(".alert-danger").First.IsVisibleAsync();
}
