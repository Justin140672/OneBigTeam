using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Contact Details tab on the self-service My Profile page.
/// Selectors derived from MyProfileContactDetailsTab.razor (.cd-* CSS classes).
/// </summary>
public sealed class ContactDetailsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".cd-card", new() { Timeout = 15_000 });

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".cd-card").IsVisibleAsync();

    // ── Field accessors ────────────────────────────────────────────────────────

    public async Task FillPersonalEmailAsync(string email)
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        await input.ClearAsync();
        await input.FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillMobilePhoneAsync(string phone)
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        await input.ClearAsync();
        await input.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillHomePhoneAsync(string phone)
    {
        var input = page.GetByPlaceholder("e.g. 01234 567890");
        await input.ClearAsync();
        await input.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillAddressLine1Async(string value)
    {
        var input = page.GetByPlaceholder("Street address");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillCityAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. London");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillPostCodeAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. SW1A 1AA");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillCountryAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. United Kingdom");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Actions ────────────────────────────────────────────────────────────────

    public async Task SaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        // Wait for the success banner to appear.
        await page.WaitForSelectorAsync(".cd-success-banner", new() { Timeout = 15_000 });
    }

    /// <summary>Clicks Save without waiting for success — for asserting a validation error instead.</summary>
    public async Task ClickSaveAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".cd-success-banner").IsVisibleAsync();

    public async Task<bool> HasValidationErrorAsync()
    {
        try
        {
            await page.Locator(".is-invalid").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// This tab has no per-field ".is-invalid" styling (see <see cref="HasValidationErrorAsync"/>) —
    /// a failed client-side Validate() instead surfaces as the GlobalError banner
    /// (EditSectionBase sets it to "Please correct the highlighted fields above.").
    /// </summary>
    public async Task<bool> HasGlobalErrorAsync() =>
        await page.Locator(".alert-danger").IsVisibleAsync();

    /// <summary>Returns the current value of the Work Email field (readonly).</summary>
    public async Task<string?> GetWorkEmailAsync()
    {
        var input = page.Locator(".cd-readonly").First;
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }

    /// <summary>Returns the current value of the Personal Email input via the live DOM property.</summary>
    public async Task<string?> GetPersonalEmailAsync()
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }
}
