using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Web's "/verify-email-error" page (VerifyEmailError.razor) — reached when
/// GET /verify-email (Program.cs) fails to exchange a verification code, either because no/an
/// invalid code was supplied or because HR.Api's POST /api/verify-email rejected it. Offers a
/// resend form that bridges over to the marketing site's existing /check-your-email page rather
/// than duplicating its resend UI.
/// </summary>
public sealed class VerifyEmailErrorPage(IPage page)
{
    private const string LoadedSelector = "h1";

    public Task WaitForLoadAsync() =>
        page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });

    public Task<bool> IsInvalidLinkMessageVisibleAsync() =>
        page.GetByText("This verification link is invalid or has expired").IsVisibleAsync();

    public async Task ResendVerificationEmailAsync(string email)
    {
        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Resend verification email" }).ClickAsync();
    }
}
