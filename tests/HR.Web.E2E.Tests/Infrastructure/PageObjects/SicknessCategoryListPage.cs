using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SicknessCategoryListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/sickness-categories");
        await page.WaitForSelectorAsync(".e-grid, .alert-danger", new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/sickness-categories/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasItemAsync(string nameFragment) =>
        await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();

    public async Task ClickEditAsync(string nameFragment)
    {
        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// "Delete" is actually a soft-deactivate (DeactivateSicknessCategory sets IsActive=false).
    /// The list defaults to active-only, so the row disappears from view after this call unless
    /// <see cref="ShowInactiveAsync"/> has been used to reveal inactive categories too.
    /// </summary>
    public async Task DeleteAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        return await row.Locator(".badge.bg-success").IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
