using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the leave policy list page (/companies/{companyId}/leave-policies).
/// </summary>
public sealed class LeavePolicyListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/leave-policies");
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 20_000 });
        await page.WaitForSpinnerToClearAsync();
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/leave-policies/new**", new() { Timeout = 30_000 });
    }

    public Task<bool> HasItemAsync(string nameFragment) =>
        page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();

    /// <summary>
    /// Checks whether the row containing <paramref name="nameFragment"/> shows the "Default"
    /// star badge.
    /// </summary>
    public async Task<bool> IsDefaultAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        return await row.Locator(".badge:has-text('Default')").IsVisibleAsync();
    }

    /// <summary>
    /// Sets the policy whose row contains <paramref name="nameFragment"/> as the company's
    /// default leave policy via the "Set as Default" toolbar action.
    /// </summary>
    public async Task SetAsDefaultAsync(string nameFragment)
    {
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();

        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Set as Default" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();

        await page.WaitForSpinnerToClearAsync();
    }
}
