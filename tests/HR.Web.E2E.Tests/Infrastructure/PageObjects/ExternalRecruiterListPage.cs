using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class ExternalRecruiterListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/external-recruiters");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        var addButton = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (page.Url.Contains("/external-recruiters/new"))
                return;

            try
            {
                await addButton.ClickAsync(new() { Timeout = 10_000 });
                await page.WaitForURLAsync("**/external-recruiters/new**", new() { Timeout = 10_000 });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
            }
        }

        await page.WaitForURLAsync("**/external-recruiters/new**", new() { Timeout = 10_000 });
    }

    public async Task<bool> HasItemAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.HasGridCellOnAnyPageAsync(agencyNameFragment);
    }

    private ILocator Row(string agencyNameFragment) =>
        page.Locator(".e-row").Filter(new() { HasText = agencyNameFragment }).First;

    public async Task<bool> IsActiveAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await page.RevealGridRowAsync(agencyNameFragment);
        return await Row(agencyNameFragment).Locator(".badge.bg-success").IsVisibleAsync();
    }

    public async Task DeactivateAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await page.RevealGridRowAsync(agencyNameFragment);

        await Row(agencyNameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task ActivateAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await page.RevealGridRowAsync(agencyNameFragment);

        await Row(agencyNameFragment).ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Activate", Exact = true });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClickRecruiterAsync(string agencyNameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await page.RevealGridRowAsync(agencyNameFragment);
        var link = page.Locator(".e-rowcell a").Filter(new() { HasText = agencyNameFragment }).First;
        await link.ClickAsync();
        await page.WaitForSelectorAsync("button:has-text('Save'), button:has-text('Back to external recruiters')", new() { Timeout = 20_000 });
    }
}
