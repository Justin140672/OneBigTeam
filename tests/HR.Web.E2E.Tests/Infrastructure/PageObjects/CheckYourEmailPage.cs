using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the marketing site's "Check your email" page (/check-your-email —
/// CheckYourEmail.razor). Reached after a successful signup (/signup-submit redirects here
/// instead of auto-logging the new admin in, since /api/signup now creates a pending Supabase
/// Auth user requiring email verification). This lives on the "marketing" Aspire resource, not
/// HR.Web, so callers should pass the marketing base URL.
/// </summary>
public sealed class CheckYourEmailPage(IPage page, string marketingBaseUrl)
{
    private const string LoadedSelector = "h1";

    public async Task GoToAsync(string email)
    {
        await page.GotoAsync($"{marketingBaseUrl}/check-your-email?email={Uri.EscapeDataString(email)}");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Waits for the page to finish loading after arriving here via a redirect (e.g. from
    /// /signup-submit or /resend-verification) rather than a direct GoToAsync.
    /// </summary>
    public Task WaitForLoadAsync() =>
        page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });

    /// <summary>
    /// Reads the registered email address rendered in the hero copy's &lt;strong&gt; tag
    /// (CheckYourEmail.razor only renders it when the "email" query param is present).
    /// </summary>
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

    /// <summary>
    /// True once the "We've sent a new verification email." confirmation renders — only shown
    /// when the "resent" query param is present (CheckYourEmail.razor's @if (Resent) block).
    /// </summary>
    public Task<bool> IsResentConfirmationVisibleAsync() =>
        page.GetByText("We've sent a new verification email.").IsVisibleAsync();

    public async Task ClickChangeEmailAddressAsync()
    {
        var link = page.GetByRole(AriaRole.Link, new() { Name = "Change email address" });

        // .site-header is `position: sticky; top: 0; z-index: 20` (see styles.css). The site has a
        // documented `section[id] { scroll-margin-top: 96px; }` rule elsewhere specifically to
        // keep that sticky header from covering scroll targets, but it only applies to sections
        // with an id (in-page anchor targets) — CheckYourEmail.razor's <section>s have none. Left
        // to its default auto-scroll, Playwright can bring this link to rest right under the
        // sticky header, which then intercepts the click's pointer-event target check and the
        // click never registers. Scroll it into view first, then nudge the page back down past the
        // header's height before clicking.
        await link.ScrollIntoViewIfNeededAsync();
        await page.Mouse.WheelAsync(0, -120);

        await link.ClickAsync();
    }
}
