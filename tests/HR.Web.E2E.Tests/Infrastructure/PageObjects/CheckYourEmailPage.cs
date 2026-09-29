using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class CheckYourEmailPage(IPage page, string marketingBaseUrl)
{
    private const string LoadedSelector = "h1";

    public async Task GoToAsync(string email)
    {
        await page.GotoAsync($"{marketingBaseUrl}/check-your-email?email={Uri.EscapeDataString(email)}");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public Task WaitForLoadAsync() =>
        page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });

    public async Task<string?> GetDisplayedEmailAsync()
    {
        var strong = page.Locator(".hero-copy strong");
        return await strong.IsVisibleAsync() ? (await strong.TextContentAsync())?.Trim() : null;
    }

    public async Task ClickResendVerificationEmailAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Resend verification email" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("resent=true"), new() { Timeout = 20_000 });
    }

    public Task<bool> IsResentConfirmationVisibleAsync() =>
        page.GetByText("We've sent a new verification email.").IsVisibleAsync();

    public async Task ClickChangeEmailAddressAsync()
    {
        var link = page.GetByRole(AriaRole.Link, new() { Name = "Change email address" });

        await link.ScrollIntoViewIfNeededAsync();
        await page.Mouse.WheelAsync(0, -120);

        await link.ClickAsync();
    }
}
