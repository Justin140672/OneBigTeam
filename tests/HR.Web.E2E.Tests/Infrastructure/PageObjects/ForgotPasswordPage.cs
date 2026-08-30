using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Web's "/forgot-password" page (ForgotPassword.razor). Interactive Server
/// page: enter an email, submit, and get a deliberately non-enumerating confirmation regardless of
/// whether the address matches an account.
/// </summary>
public sealed class ForgotPasswordPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/forgot-password");
        await page.Locator("[placeholder='you@example.com']").WaitForAsync(new() { Timeout = 30_000 });
    }

    public async Task SubmitAsync(string email)
    {
        await page.Locator("[placeholder='you@example.com']").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send reset link" }).ClickAsync();
    }

    public async Task<string> GetConfirmationTextAsync()
    {
        var locator = page.Locator("[data-testid='forgot-password-confirmation']");
        await locator.WaitForAsync(new() { Timeout = 30_000 });
        return (await locator.InnerTextAsync()).Trim();
    }
}
