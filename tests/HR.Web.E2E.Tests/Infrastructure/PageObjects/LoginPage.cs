using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class LoginPage(IPage page, string baseUrl)
{
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    // "Successfully authenticated" signal. Normally the app shell — but a brand-new company's
    // initial admin (RequiresInitialSetup = true) is deliberately NEVER shown the shell:
    // MainLayout.razor renders ONLY the blocking EmployeeCompletionDialog for them. Treating that
    // as a valid post-login state lets EmployeeCompletionDialogTests' fresh-signup logins succeed
    // instead of timing out 5× waiting for a shell that will never appear.
    private const string AuthenticatedSelector = ".app-shell, .employee-completion-dialog";

    public async Task GoToAsync()
    {
        using var totalTimer = E2eDiag.Time("LoginPage", "GoToAsync total");
        var gotoTimer = E2eDiag.Time("LoginPage", "GoToAsync: GotoAsync(/login, Commit)");
        await page.GotoAsync($"{baseUrl}/login", new()
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = 60_000,
        });
        gotoTimer.Dispose();

        var pollTimer = E2eDiag.Time("LoginPage", "GoToAsync: poll for login form / app shell (30s budget)");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (await page.Locator("[placeholder='you@example.com']").IsVisibleAsync()) { pollTimer.Dispose(); return; }
            if (await page.Locator(AuthenticatedSelector).First.IsVisibleAsync()) { pollTimer.Dispose(); return; }
            if (DateTime.UtcNow > deadline)
            {
                pollTimer.Dispose();
                throw new TimeoutException("Timed out waiting for the login form or app shell after navigating to /login.");
            }
            await Task.Delay(100);
        }
    }

    public async Task LoginAsync(string email, string password = DevPersonaPassword)
    {
        if (await page.Locator(AuthenticatedSelector).First.IsVisibleAsync())
        {
            if (await IsAuthenticatedAsAsync(email)) return;

            await page.Context.ClearCookiesAsync();
            await page.GotoAsync($"{baseUrl}/login", new() { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
            await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
        }

        var browser = page.Context.Browser;
        if (browser is not null && await TryCachedLoginAsync(browser, email))
            return;

        await RealFormLoginAsync(email, password);

        if (browser is not null)
        {
            await PersonaLoginCache.PublishAsync(email, page);
        }
    }

    private async Task<bool> TryCachedLoginAsync(IBrowser browser, string email)
    {
        var (options, entry) = await PersonaLoginCache.GetOrLoginWithEntryForCallerAsync(browser, baseUrl, email);
        if (options.StorageState is string json && await PersonaLoginCache.TryApplyStorageStateAsync(page, baseUrl, json))
            return true;

        var refreshed = await PersonaLoginCache.InvalidateAndRefreshAsync(browser, baseUrl, email, entry);
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
        using var totalTimer = E2eDiag.Time("LoginPage", $"RealFormLoginAsync({email}) total");

        if (await page.Locator(AuthenticatedSelector).First.IsVisibleAsync())
        {
            await page.Context.ClearCookiesAsync();
            await page.GotoAsync($"{baseUrl}/login", new() { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
            await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
        }

        var fillTimer = E2eDiag.Time("LoginPage", $"RealFormLoginAsync({email}): fill + submit form");
        await page.GetByPlaceholder("you@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByPlaceholder("••••••••").FillAsync(password);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        fillTimer.Dispose();

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
        using var waitTimer = E2eDiag.Time("LoginPage", $"RealFormLoginAsync({email}): wait for app shell / login error (45s budget)");
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (true)
        {
            if (await page.Locator(AuthenticatedSelector).First.IsVisibleAsync()) return;

            var errorLocator = page.Locator(".login-error");
            if (await errorLocator.IsVisibleAsync())
            {
                var errorText = (await errorLocator.InnerTextAsync())?.Trim();
                E2eDiag.Log("LoginPage", $"RealFormLoginAsync({email}): rejected — \"{errorText}\"");
                throw new InvalidOperationException(
                    $"Login for '{email}' was rejected instead of reaching the app shell: \"{errorText}\"");
            }

            if (DateTime.UtcNow > deadline)
            {
                E2eDiag.Log("LoginPage", $"RealFormLoginAsync({email}): 45s app-shell wait TIMED OUT — this is the app itself not rendering the shell in time, not a credential/Supabase rejection");
                throw new TimeoutException(
                    $"Timed out waiting for the app shell (or a login error) after submitting real credentials for '{email}'. " +
                    await DescribePageStateAsync());
            }

            await Task.Delay(100);
        }
    }

    private async Task<string> DescribePageStateAsync()
    {
        static async Task<string> Safe(Func<Task<string?>> read)
        {
            try { return await read() ?? "(null)"; }
            catch (Exception ex) { return $"(unavailable: {ex.GetType().Name})"; }
        }

        var url = page.Url;
        var title = await Safe(async () => await page.TitleAsync());
        var headings = await Safe(async () =>
        {
            var texts = await page.Locator("h1:visible, h2:visible").AllInnerTextsAsync();
            return string.Join(" | ", texts.Select(t => t.Trim()).Where(t => t.Length > 0));
        });
        var body = await Safe(async () =>
        {
            var text = (await page.Locator("body").InnerTextAsync(new() { Timeout = 3_000 })).Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            return text.Length > 300 ? text[..300] : text;
        });
        var cookies = await Safe(async () =>
        {
            var all = await page.Context.CookiesAsync();
            return all.Count == 0 ? "none" : string.Join(",", all.Select(c => c.Name));
        });
        var markers = await Safe(async () =>
        {
            var loading = await page.Locator(".app-loading").CountAsync();
            var shell = await page.Locator(".app-shell").CountAsync();
            var dialog = await page.Locator(".employee-completion-dialog").CountAsync();
            var busy = await page.Locator(".login-btn .spinner-border").CountAsync();
            return $"app-loading={loading}, app-shell={shell}, completion-dialog={dialog}, login-busy-spinner={busy}";
        });

        return $"Page state: url={url}; title=\"{title}\"; headings=\"{headings}\"; cookies=[{cookies}]; {markers}; body=\"{body}\"";
    }

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

    private static bool IsPersonaShapedEmail(string email)
    {
        var parts = email.Split('@')[0].Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts.All(p => p.All(char.IsLetter));
    }

    private static string DerivePersonaDisplayName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public async Task SwitchAccountAsync(string email, string password = DevPersonaPassword)
    {
        await page.GotoAsync($"{baseUrl}/login", new() { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
        await LoginAsync(email, password);

        if (IsPersonaShapedEmail(email) && !await IsAuthenticatedAsAsync(email))
        {
            await page.Context.ClearCookiesAsync();
            await page.GotoAsync($"{baseUrl}/login", new() { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
            await page.WaitForSelectorAsync("[placeholder='you@example.com']", new() { Timeout = 30_000 });
            await LoginAsync(email, password);

            if (!await IsAuthenticatedAsAsync(email))
                throw new InvalidOperationException(
                    $"Switching account to '{email}' did not take effect (page is at {page.Url}).");
        }
    }

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
        await DropDownSelector.SelectAsync(page, page.Locator(".dev-persona-switcher"), personaNameFragment);
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }
}
