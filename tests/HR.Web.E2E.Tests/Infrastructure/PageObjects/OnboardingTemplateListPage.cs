using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class OnboardingTemplateListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/onboarding-templates");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/onboarding-templates/new**", new() { Timeout = 15_000 });
    }

    public async Task<string> GetRowHrefAsync(string nameFragment)
    {
        await page.RevealGridRowAsync(nameFragment);
        var href = await page.Locator(".e-rowcell a").Filter(new() { HasText = nameFragment }).First.GetAttributeAsync("href");
        return href ?? throw new InvalidOperationException($"No onboarding-template row link found for '{nameFragment}'.");
    }

    public async Task<bool> HasItemAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public async Task DeactivateAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await page.RevealGridRowAsync(nameFragment);

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        var confirmButton = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Yes", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        await page.RevealGridRowAsync(nameFragment);

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
