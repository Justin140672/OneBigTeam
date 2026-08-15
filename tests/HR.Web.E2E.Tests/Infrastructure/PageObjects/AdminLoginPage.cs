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
        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByPlaceholder("password").FillAsync(password);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // SubmitAsync redirects via window.location.href to /dev/persona-cookie, which itself
        // redirects to "/" — wait for navigation away from /login rather than for any specific
        // authorised content, since an authenticated-but-not-allow-listed persona still lands on
        // "/" (just with the unauthorised dashboard-error state).
        await page.WaitForURLAsync(url => !url.ToString().Contains("/login"), new() { Timeout = 30_000 });
    }
}
