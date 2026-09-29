using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class DepartmentListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/departments");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewDepartmentAsync()
    {
        // Retry the click rather than a single fire-and-wait — see
        // EmployeeListPage.ClickNewEmployeeAsync's remarks for why a fixed single-shot timeout
        // genuinely isn't enough under a full parallel E2E run.
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // ClickAsync must be inside the try too — see EmployeeListPage.ClickNewEmployeeAsync's
                // remarks for why an unwrapped ClickAsync (default 30s actionability wait) can
                // escape the retry loop entirely and look like an unretried 30000ms timeout.
                await button.ClickAsync(new() { Timeout = attempt < maxAttempts ? 5_000 : 30_000 });
                await page.WaitForURLAsync("**/departments/new**", new() { Timeout = attempt < maxAttempts ? 3_000 : 15_000 });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
        }
    }

    public async Task<bool> HasDepartmentAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.HasGridCellOnAnyPageAsync(nameFragment);
    }

    public async Task<string> GetRowHrefAsync(string nameFragment)
    {
        var href = await page.Locator(".e-rowcell a").Filter(new() { HasText = nameFragment }).First.GetAttributeAsync("href");
        return href ?? throw new InvalidOperationException($"No department row link found for '{nameFragment}'.");
    }

    public async Task<IReadOnlyList<string>> GetDepartmentNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task DeactivateDepartmentAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        var badge = row.Locator(".status-badge.status-badge--success");
        return await badge.IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }
}
