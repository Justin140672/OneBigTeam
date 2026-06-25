using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the company edit page (/companies/{id}/edit).
/// Covers the Profile and Settings tabs.
/// </summary>
public sealed class CompanyEditPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/edit");
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    // ── Tab navigation ─────────────────────────────────────────────────────────

    public async Task OpenProfileTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Profile" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    public async Task OpenSettingsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Settings" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    public async Task OpenAddressesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Addresses" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    // ── Profile tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the company name shown in the h1 heading.</summary>
    public async Task<string> GetCompanyNameAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";

    public async Task<bool> IsActiveAsync() =>
        await page.Locator("h1 ~ .badge.bg-success, .badge.bg-success").First.IsVisibleAsync();

    // ── Settings tab ───────────────────────────────────────────────────────────

    /// <summary>Returns true if a working-week day checkbox (e.g. "Monday") is currently checked.</summary>
    public async Task<bool> IsWorkingDayCheckedAsync(string dayName)
    {
        var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
        // The associated checkbox precedes the label in the working-week group.
        var checkbox = label.Locator("input[type='checkbox']");
        return await checkbox.IsCheckedAsync();
    }

    // ── Save ───────────────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger").First.IsVisibleAsync();
}
