using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the department list page (/companies/{companyId}/departments).
/// </summary>
public sealed class DepartmentListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/departments");
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

    public async Task ClickNewDepartmentAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/departments/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasDepartmentAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetDepartmentNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Deactivates the department whose row contains <paramref name="nameFragment"/>
    /// by clicking the deactivate toolbar action.
    /// </summary>
    public async Task DeactivateDepartmentAsync(string nameFragment)
    {
        // Select the row first, then click the deactivate toolbar button.
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        // The toolbar deactivate button uses a fa-circle-xmark icon.
        await page.Locator(".e-toolbar-item[title='Deactivate']").ClickAsync();
        // Wait for the grid to refresh.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        var badge = row.Locator(".badge.bg-success");
        return await badge.IsVisibleAsync();
    }
}
