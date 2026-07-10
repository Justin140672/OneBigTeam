using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for a single import session's detail page: status, row counts, and (when there
/// are failed rows) the Download Error Report affordance.
/// </summary>
public sealed class ImportSessionDetailPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid importSessionId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/data-import/sessions/{importSessionId}");
        await page.WaitForSelectorAsync(".card, .alert-warning", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// The "Status" value, which is the first &lt;dt&gt;/&lt;dd&gt; pair rendered in the
    /// session's details list.
    /// </summary>
    public async Task<string> GetStatusAsync() => await page.Locator("dl.row dd").First.InnerTextAsync();
}
