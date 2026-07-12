using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee list page (/companies/{companyId}/employees).
/// </summary>
public sealed class EmployeeListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees");
        // Prerender delivers .e-grid immediately. After circuit connects, SearchPageBase sets
        // IsLoading=true (briefly shows .spinner-border, removes .e-grid), then re-fetches
        // and restores .e-grid. We watch for that spinner→grid cycle to confirm the circuit
        // is connected and toolbar OnToolbarClick handlers are wired up.
        await page.EvaluateAsync(@"() => {
            window._listReady = false;
            let spinnerSeen = document.querySelector('.spinner-border') !== null;
            const obs = new MutationObserver(() => {
                if (!spinnerSeen && document.querySelector('.spinner-border')) {
                    spinnerSeen = true;
                }
                if (spinnerSeen && !document.querySelector('.spinner-border') &&
                    document.querySelector('.e-grid')) {
                    window._listReady = true;
                    obs.disconnect();
                }
            });
            obs.observe(document.body, { subtree: true, childList: true });
        }");
        await page.WaitForFunctionAsync(
            "window._listReady === true",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
    }

    public async Task ClickNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/employees/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasEmployeeAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetEmployeeNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task ClickEmployeeAsync(string nameFragment)
    {
        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
        // The edit page shows a spinner while its LoadAsync() runs; without this wait, callers
        // that immediately assert on page content (e.g. tab visibility) can race the load and
        // observe the page still in its loading state.
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 15_000 });
    }

    public async Task SearchAsync(string query)
    {
        var searchInput = page.GetByPlaceholder("Search by name or email");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        // Syncfusion grid filters on input change.
        await page.WaitForTimeoutAsync(500);
    }
}
