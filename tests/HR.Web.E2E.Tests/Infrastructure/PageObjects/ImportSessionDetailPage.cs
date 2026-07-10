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

    /// <summary>Returns the &lt;dd&gt; value for the detail row whose &lt;dt&gt; matches <paramref name="label"/>.</summary>
    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = page.Locator("dl.row dt").Filter(new() { HasText = label }).First;
        if (!await dt.IsVisibleAsync()) return null;
        return (await dt.Locator("~ dd").First.InnerTextAsync())?.Trim();
    }

    public async Task<bool> IsDownloadErrorReportButtonVisibleAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Download Error Report" }).IsVisibleAsync();
}
