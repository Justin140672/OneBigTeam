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

    public Task<bool> IsInvalidLinkMessageVisibleAsync() =>
        page.GetByText("This password reset link is no longer valid. Please request a new one.").IsVisibleAsync();

    public Task ClickBackToForgotPasswordAsync() =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to Forgot Password" }).ClickAsync();
}
