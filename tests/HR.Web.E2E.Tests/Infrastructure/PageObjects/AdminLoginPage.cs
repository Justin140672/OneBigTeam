using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's Login.razor (/login) — the internal Admin Portal's
/// development-only sign-in page. Unlike HR.Web's LoginPage (which now does a real Supabase
/// password-grant sign-in), this still uses the older dev-persona-switch stub: any seeded
/// Development persona email plus the literal password "password" (see Login.razor's
/// SubmitAsync, which checks `_form.Password != "password"` before calling
/// DevAuthService.SwitchAsync). Being on this allow-list of seeded personas is not the same
/// as being platform-admin-authorised — that's a separate server-side check
/// ("PlatformAdmin:AllowedEmails" in configuration) performed on every subsequent API call, so a
/// successful login here can still land on an unauthorised state once a protected page loads.
/// </summary>
public sealed class AdminLoginPage(IPage page, string baseUrl)
{
    private const string DevPersonaLoginPassword = "password";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/login");
        await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Logs in as a seeded Development persona and waits for the post-login redirect to
    /// complete. This only proves the persona exists and the dev-login stub accepted it — it
    /// does not by itself prove platform-admin authorisation (see class remarks).
    /// </summary>
    public async Task LoginAsync(string email, string password = DevPersonaLoginPassword)
    {
        // SfTextBox's server-side bound value (_form.Email/_form.Password) only round-trips over
        // the Blazor Server circuit on blur/change — not on FillAsync's raw "input" DOM event
        // alone (same convention documented elsewhere in this suite, e.g.
        // ReportCatalogPage.SaveCurrentFiltersAsNewViewAsync). Without a Tab after each fill,
        // clicking Sign in can race that round-trip and submit with the server-side model still
        // holding its previous (empty) value, which looks exactly like "not entering" the
        // credentials — the fields visibly show the typed text, but the server never saw it.
        await SubmitCredentialsAsync(email, password);

        // SubmitAsync redirects via window.location.href to /dev/persona-cookie, which itself
        // redirects to "/". A platform-admin-authorised persona ends up off /login.
        await page.WaitForURLAsync(url => !url.ToString().Contains("/login"), new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Submits the login form for an account that is NOT platform-admin-authorised. Login.razor
    /// rejects it inline (see its IsPlatformAdminAsync gate) — the page stays on /login and shows
    /// the ".login-error" message. Returns that message text.
    /// </summary>
    public async Task<string> SubmitExpectingNotAuthorisedAsync(string email, string password = DevPersonaLoginPassword)
    {
        await SubmitCredentialsAsync(email, password);

        var error = page.Locator(".login-error");
        await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        return (await error.TextContentAsync())?.Trim() ?? "";
    }

    /// <summary>True while the browser is still on the Admin Portal login page.</summary>
    public bool IsOnLoginPage() => page.Url.Contains("/login");

    private async Task SubmitCredentialsAsync(string email, string password)
    {
        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByPlaceholder("password").FillAsync(password);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
