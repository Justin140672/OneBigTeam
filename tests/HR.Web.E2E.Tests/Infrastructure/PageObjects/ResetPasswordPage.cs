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

    // ResetPasswordComplete.razor sets @rendermode with prerender:false — the page's real markup
    // (this invalid-link text included) doesn't exist until the Blazor Server circuit has actually
    // connected and rendered interactively, which happens strictly after the URL itself has already
    // landed on "/reset-password-complete" (the signal the test waits on before calling this). A
    // bare instant IsVisibleAsync() here races that circuit connection and can — deterministically,
    // not just occasionally — return false before the circuit finishes, since a non-retrying check
    // returns immediately regardless of how likely the underlying condition is to appear a moment
    // later. Wait for it instead.
    public async Task<bool> IsInvalidLinkMessageVisibleAsync()
    {
        var message = page.GetByText("This password reset link is no longer valid. Please request a new one.");

        // OnInitialized redeems the single-use handoff code exactly once (see the class remarks on
        // @rendermode prerender:false) — that redemption, and therefore this text, only exists once
        // the interactive circuit has actually connected after the /reset-password-begin redirect
        // landed on this page. Under parallel E2E load that first circuit connect can occasionally
        // take longer than a single short wait; a bounded reload-and-retry recovers from a
        // genuinely-slow-but-not-broken connect without masking a real rendering failure (each
        // attempt still requires the text to actually appear, not just tolerate a timeout).
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await message.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < 3 ? 10_000 : 20_000 });
                return true;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                await page.ReloadAsync();
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        return false;
    }

    public Task ClickBackToForgotPasswordAsync() =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to Forgot Password" }).ClickAsync();
}
