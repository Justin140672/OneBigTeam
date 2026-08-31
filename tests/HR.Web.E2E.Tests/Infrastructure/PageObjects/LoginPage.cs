using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class LoginPage(IPage page, string baseUrl)
{
    // Login.razor now performs a real Supabase password-grant sign-in (HR.Modules.Identity's
    // Login feature, POST /api/login) rather than the earlier dev-persona stub that accepted any
    // seeded email with the literal password "password". Every seeded Development persona still
    // has a real Supabase account (see IdentityModule.SeedDevSupabaseUsersAsync) — just under
    // this actual password. Canonical definition:
    // HR.Modules.Identity.Services.SupabaseAuthGateway.DevSupabasePassword (internal, not
    // referenceable from this project).
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/login");

        // Either the login form renders (fresh/unauthenticated context) or, if this context was
        // built from a role fixture's storageState (see RolePersonaFixtureBase), the app redirects
        // straight past /login to the shell because a session cookie is already present. Wait for
        // whichever shows up first instead of always blocking for the full form-render timeout.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (await page.Locator("[placeholder='you@example.com']").IsVisibleAsync()) return;
            if (await page.Locator(".app-shell").IsVisibleAsync()) return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for the login form or app shell after navigating to /login.");
            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Logs in as <paramref name="email"/>, preferring a cached Playwright storageState (see
    /// <see cref="PersonaLoginCache"/>) over a real interactive form login. A real login only ever
    /// happens once per persona for the whole test run — the first caller for a given persona (or a
    /// caller whose cached state turned out stale) pays for it, and it gets cached for everyone else,
    /// no matter which xUnit collection or test class asks. There is no test in this suite whose
    /// subject-under-test is the login FORM itself, so unconditionally preferring the cache here is
    /// safe — nothing depends on this method actually driving the form UI.
    /// </summary>
    public async Task LoginAsync(string email, string password = DevPersonaPassword)
    {
        if (await page.Locator(".app-shell").IsVisibleAsync())
        {
            // Already authenticated — either a role fixture's storageState landed us straight on
            // the shell, or an earlier LoginAsync call in this same test already logged in. If it's
            // for the SAME persona we're being asked to log in as, there's nothing left to do — that
            // is the entire point of storageState reuse.
            if (await IsAuthenticatedAsAsync(email)) return;

            // A different persona is authenticated than requested (a role-fixed collection's default
            // persona while this specific test wants an outlier persona, e.g. an access-denied
            // check). Clear the session and fall through to a login for the requested persona.
            await page.Context.ClearCookiesAsync();
            await page.GotoAsync($"{baseUrl}/login");
            await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
        }

        var browser = page.Context.Browser;
        if (browser is not null && await TryCachedLoginAsync(browser, email))
            return;

        // Cache unavailable or exhausted its one refresh attempt — genuine last-resort real login.
        await RealFormLoginAsync(email, password);

        if (browser is not null)
        {
            // Publish this freshly-good session so subsequent callers for this persona (this was
            // presumably a previously-unseen or twice-stale persona) get the cache speed-up too.
            await PersonaLoginCache.PublishAsync(email, page);
        }
    }

    /// <summary>
    /// Tries the cached storageState for <paramref name="email"/>; if applying it doesn't reach the
    /// authenticated shell, invalidates the cache entry and tries exactly one fresh real login before
    /// giving up on the cache for this call (the caller then falls back to a direct real login on this
    /// page). Guards against a stale/expired Supabase session being served indefinitely.
    /// </summary>
    private async Task<bool> TryCachedLoginAsync(IBrowser browser, string email)
    {
        var options = await PersonaLoginCache.GetOrLoginAsync(browser, baseUrl, email);
        if (options.StorageState is string json && await PersonaLoginCache.TryApplyStorageStateAsync(page, baseUrl, json))
            return true;

        PersonaLoginCache.Invalidate(email);
        var refreshed = await PersonaLoginCache.GetOrLoginAsync(browser, baseUrl, email);
        return refreshed.StorageState is string refreshedJson &&
            await PersonaLoginCache.TryApplyStorageStateAsync(page, baseUrl, refreshedJson);
    }

    /// <summary>
    /// The real interactive form login — always drives the actual UI, never the cache. Used directly
    /// by <see cref="PersonaLoginCache"/>'s bootstrap login (which IS the code path that populates the
    /// cache, so it must not recurse back into <see cref="LoginAsync"/>) and as <see cref="LoginAsync"/>'s
    /// own last-resort fallback when cached storageState can't be made to work.
    /// </summary>
    internal async Task RealFormLoginAsync(string email, string password = DevPersonaPassword)
    {
        if (await page.Locator(".app-shell").IsVisibleAsync())
        {
            await page.Context.ClearCookiesAsync();
            await page.GotoAsync($"{baseUrl}/login");
            await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
        }

        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByPlaceholder("••••••••").FillAsync(password);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();

        // Wait for whichever of "shell loaded" or "Login.razor's own inline error banner shown"
        // happens first, rather than only waiting for the shell — a real credential/Supabase
        // rejection surfaces near-instantly as ".login-error", so distinguishing that from a
        // genuine timeout gives a far more actionable failure message than a bare 30s
        // TimeoutException on ".app-shell" alone (which looks identical whether the account
        // simply hasn't finished propagating on Supabase's side yet, or login is outright
        // rejected). Bumped 30s -> 45s: this path (real, uncached, one-time Supabase
        // password-grant logins — see PersonaLoginCache's per-persona-once-per-run caching, which
        // this bootstrap path exists to populate) is the same real-Supabase-network dependency
        // already flagged as a genuine infra bottleneck for signup/AdminUsersManagementTests
        // under this suite's current concurrency; every fresh, single-use employee login (asset
        // acknowledge/return, self-service document upload) pays this same real, uncacheable cost
        // on every run.
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (true)
        {
            if (await page.Locator(".app-shell").IsVisibleAsync()) return;

            var errorLocator = page.Locator(".login-error");
            if (await errorLocator.IsVisibleAsync())
            {
                var errorText = (await errorLocator.InnerTextAsync())?.Trim();
                throw new InvalidOperationException(
                    $"Login for '{email}' was rejected instead of reaching the app shell: \"{errorText}\"");
            }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"Timed out waiting for the app shell (or a login error) after submitting real credentials for '{email}'.");

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Best-effort check of whether the currently authenticated user matches <paramref name="email"/>,
    /// used to skip redundant logins when a context is already authenticated via storageState. Dev
    /// seed personas are consistently named "firstname.lastname@..." (see DevPersonaStore), and the
    /// topbar renders that same "Firstname Lastname" as Session.DisplayName (MainLayout.razor), so we
    /// can compare without any app-side test hook. If the topbar user block isn't present (e.g. a
    /// persona with no linked employee record, or the shell hasn't finished its first render yet),
    /// this conservatively returns false, which just means a real login runs instead of being skipped
    /// — slower, never incorrect.
    /// </summary>
    private async Task<bool> IsAuthenticatedAsAsync(string email)
    {
        var expectedName = DerivePersonaDisplayName(email);
        var userInfo = page.Locator(".top-bar-user-info");
        try
        {
            await userInfo.WaitForAsync(new() { Timeout = 3_000 });
            if (!await userInfo.IsVisibleAsync()) return false;
            var actual = await userInfo.InnerTextAsync();
            return actual.Contains(expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string DerivePersonaDisplayName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public async Task SwitchAccountAsync(string email, string password = DevPersonaPassword)
    {
        // In dev mode, persona switcher is an SfDropDownList in the topbar — we don't have a
        // userId mapping from an email here, so this always falls back to cookie-based login.
        await page.GotoAsync($"{baseUrl}/login");
        await page.WaitForURLAsync($"{baseUrl}/login");
        await LoginAsync(email, password);
    }

    /// <summary>
    /// The compact legal / trust link row rendered beneath the login form
    /// (<c>&lt;nav aria-label="Legal and policies"&gt;</c> in Login.razor). Returns the visible
    /// link text paired with its resolved <c>href</c>, in document order.
    /// </summary>
    public async Task<IReadOnlyList<(string Text, string Href)>> GetLegalLinksAsync()
    {
        var links = page.Locator("[data-testid='login-legal'] a");
        var count = await links.CountAsync();
        var result = new List<(string, string)>(count);
        for (var i = 0; i < count; i++)
        {
            var link = links.Nth(i);
            var text = (await link.InnerTextAsync()).Trim();
            var href = await link.GetAttributeAsync("href") ?? string.Empty;
            result.Add((text, href));
        }

        return result;
    }

    public async Task SwitchPersonaAsync(string personaNameFragment)
    {
        // Dev-mode topbar persona switcher — Syncfusion SfDropDownList, see DropDownSelector.
        await DropDownSelector.SelectAsync(page, page.Locator(".dev-persona-switcher"), personaNameFragment);
        // Selecting triggers a full navigation, wait for the shell to reload.
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }
}
