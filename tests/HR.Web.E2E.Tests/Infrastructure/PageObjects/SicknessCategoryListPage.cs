using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SicknessCategoryListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/sickness-categories");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
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
                await page.WaitForURLAsync("**/sickness-categories/new**", new() { Timeout = attempt < maxAttempts ? 3_000 : 15_000 });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
        }
    }

    public async Task<bool> HasItemAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public async Task ClickEditAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task DeleteAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        return await row.Locator(".badge.bg-success").IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }
}
