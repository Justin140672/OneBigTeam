using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Web's password-reset completion page (ResetPasswordComplete.razor, reached
/// via the "/reset-password" fragment hop in Program.cs). With no valid Supabase recovery token in
/// the URL fragment it renders the "link no longer valid" panel with a route back to Forgot
/// Password; with a token it renders the New Password / Confirm New Password form.
/// </summary>
public sealed class ResetPasswordPage(IPage page, string baseUrl)
{
    public Task GoToWithoutTokenAsync() =>
        page.GotoAsync($"{baseUrl}/reset-password");

    public Task GoToCompleteWithTokenAsync(string accessToken) =>
        page.GotoAsync($"{baseUrl}/reset-password-complete?access_token={Uri.EscapeDataString(accessToken)}");

    public Task<bool> IsInvalidLinkMessageVisibleAsync() =>
        page.GetByText("This password reset link is no longer valid. Please request a new one.").IsVisibleAsync();

    public Task ClickBackToForgotPasswordAsync() =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to Forgot Password" }).ClickAsync();

    public async Task WaitForFormAsync()
    {
        await page.GetByText("Choose a new password").WaitForAsync(new() { Timeout = 30_000 });
    }

    public async Task SubmitNewPasswordAsync(string newPassword, string confirmPassword)
    {
        var boxes = page.Locator("input[type='password']");
        await boxes.Nth(0).FillAsync(newPassword);
        await boxes.Nth(1).FillAsync(confirmPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Update Password" }).ClickAsync();
    }

    public Task<bool> IsPasswordsDoNotMatchVisibleAsync() =>
        page.GetByText("Passwords do not match.").IsVisibleAsync();
}
