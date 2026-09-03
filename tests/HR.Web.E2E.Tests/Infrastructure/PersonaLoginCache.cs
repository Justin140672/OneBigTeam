using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Process-wide cache that performs the real Supabase UI login for a given persona email exactly
/// once per test run, no matter how many <see cref="RolePersonaFixtureBase"/> instances (one per
/// role-fixed test CLASS, not one per role — see that type's remarks for why) ask for the same
/// persona concurrently across parallel classes.
///
/// Keyed by persona email via <see cref="ConcurrentDictionary{TKey,TValue}"/> of
/// <see cref="Lazy{T}"/>-wrapped login tasks: the first caller for a given email creates and starts
/// the login task, every other (possibly racing) caller for that same email observes the
/// already-published <see cref="Lazy{T}"/> and awaits the same in-flight or already-completed task
/// instead of starting its own real login. This mirrors the reference-counted singleton pattern
/// <see cref="SharedAppFixture"/> uses for the Aspire app, applied here to a per-key cache instead of
/// a single instance.
///
/// The cached <see cref="BrowserNewContextOptions"/> only carries Playwright storageState (cookies +
/// localStorage snapshot) captured from the bootstrap context immediately after login — it holds no
/// reference to that bootstrap <see cref="IBrowserContext"/>/<see cref="IPage"/> or any other
/// browser/context-instance-specific state, so it is safe to hand the same options to
/// <see cref="IBrowser.NewContextAsync"/> repeatedly, concurrently, from different classes and
/// threads, to create independent new contexts against the one shared <see cref="IBrowser"/>.
/// </summary>
internal static class PersonaLoginCache
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<BrowserNewContextOptions>>> _cache = new();

    // At run start, many distinct personas (every canonical role plus every outlier persona used
    // anywhere in the suite) can all attempt their once-per-run real login within the same instant,
    // since dozens of test classes across ~16 parallel threads spin up together. Without a cap, that
    // burst of concurrent real /login navigations can itself overwhelm the single dev app instance
    // before it's had a chance to serve anything, producing the exact same ".app-shell"/navigation
    // timeouts this cache exists to avoid — just moved from "every test" to "every distinct persona,
    // all at once" instead of spread across the whole run. Gating real logins to a small number in
    // flight lets them queue and land one after another instead of stampeding.
    // Raised 3 -> 6 alongside the Postgres/Npgsql capacity bumps (AppHost max_connections=500,
    // HR.Api pool ceiling 400 under E2E): the gate exists to stop the run-start login stampede from
    // overwhelming the shared app, not to hold logins to a trickle. With the DB no longer the
    // bottleneck, 6 concurrent bootstrap logins clear the ~15-persona backlog roughly twice as fast
    // without re-introducing the app-shell timeouts this cap was added to prevent.
    private static readonly SemaphoreSlim _realLoginGate = new(6, 6);

    public static Task<BrowserNewContextOptions> GetOrLoginAsync(AppFixture app, string personaEmail) =>
        GetOrLoginAsync(app.Browser, app.WebBaseUrl, personaEmail);

    /// <summary>
    /// Same per-persona-once-per-run login as the <see cref="AppFixture"/> overload, but usable from
    /// anywhere an <see cref="IBrowser"/> and base URL are already available (e.g. from an existing
    /// <see cref="IPage"/>'s <c>Context.Browser</c> inside <see cref="PageObjects.LoginPage"/>) rather
    /// than only at fixture-construction time — this is what lets ANY persona, not just the 4
    /// canonical role personas, get the once-per-run login treatment.
    /// </summary>
    public static async Task<BrowserNewContextOptions> GetOrLoginAsync(IBrowser browser, string baseUrl, string personaEmail)
    {
        var entry = _cache.GetOrAdd(
            personaEmail,
            email => new Lazy<Task<BrowserNewContextOptions>>(
                () => LoginAndCaptureStorageStateAsync(browser, baseUrl, email),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await entry.Value;
        }
        catch
        {
            // A Lazy<Task<T>> permanently caches a FAULTED task once its factory throws — every future
            // caller for this persona would otherwise get the exact same exception rethrown forever,
            // even if the original failure was transient (a single real Supabase hiccup, rate limit,
            // etc.). One bad login used to cascade into every test using that persona for the rest of
            // the run (mirrors the SharedAppFixture "half-initialized instance stuck forever" bug fixed
            // earlier). Remove the poisoned entry — but only if it's still the SAME failed Lazy we just
            // awaited (another caller may have already replaced it, e.g. via PublishAsync/a fresh retry)
            // — so the NEXT caller gets a genuinely fresh attempt instead of inheriting this failure.
            ((ICollection<KeyValuePair<string, Lazy<Task<BrowserNewContextOptions>>>>)_cache)
                .Remove(new KeyValuePair<string, Lazy<Task<BrowserNewContextOptions>>>(personaEmail, entry));
            throw;
        }
    }

    /// <summary>
    /// Drops a persona's cached storageState, e.g. because applying it to a page failed to reach the
    /// authenticated shell (likely a stale/expired Supabase session). The next
    /// <see cref="GetOrLoginAsync(IBrowser,string,string)"/> call for that persona performs a fresh
    /// real login instead of handing out the (apparently no-longer-valid) cached one.
    /// </summary>
    public static void Invalidate(string personaEmail) => _cache.TryRemove(personaEmail, out _);

    /// <summary>
    /// Publishes an already-authenticated page's current storageState as the cache entry for
    /// <paramref name="personaEmail"/>, overwriting whatever was cached before. Used after a genuine
    /// last-resort real login (both the cached copy and its one refresh attempt failed to apply) so
    /// later callers for the same persona benefit from this freshly-good session instead of repeating
    /// the same failure.
    /// </summary>
    public static async Task PublishAsync(string personaEmail, IPage page)
    {
        var storageState = await page.Context.StorageStateAsync();
        var options = new BrowserNewContextOptions { StorageState = storageState };
        _cache[personaEmail] = new Lazy<Task<BrowserNewContextOptions>>(
            () => Task.FromResult(options), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Applies a cached Playwright storageState JSON (cookies + per-origin localStorage, as produced
    /// by <see cref="IBrowserContext.StorageStateAsync()"/>) to an EXISTING page's context, then
    /// reloads and waits for the app shell. Unlike <see cref="IBrowser.NewContextAsync"/>'s
    /// <c>StorageState</c> option (only usable at context-creation time, which is all
    /// <see cref="RolePersonaFixtureBase"/> needs), this lets an already-open page/context adopt a
    /// cached session mid-test.
    ///
    /// Cookies apply immediately via <see cref="IBrowserContext.AddCookiesAsync"/>. localStorage
    /// cannot be set directly on a context — it can only be written from a page already on the target
    /// origin — so it is injected via <see cref="IPage.AddInitScriptAsync(string, object)"/>, which runs
    /// before every subsequent document (including the reload below) gets its first script, ensuring
    /// the values are present before Blazor's own startup script reads auth state.
    ///
    /// Returns false (instead of throwing) if the app shell doesn't show up within a short timeout,
    /// so the caller can treat the cached state as stale and fall back to a real login.
    /// </summary>
    public static async Task<bool> TryApplyStorageStateAsync(IPage page, string baseUrl, string storageStateJson)
    {
        using var doc = JsonDocument.Parse(storageStateJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("cookies", out var cookiesElement) && cookiesElement.ValueKind == JsonValueKind.Array)
        {
            var cookies = new List<Cookie>();
            foreach (var c in cookiesElement.EnumerateArray())
            {
                var cookie = new Cookie
                {
                    Name = c.GetProperty("name").GetString()!,
                    Value = c.GetProperty("value").GetString()!,
                    Domain = c.TryGetProperty("domain", out var d) ? d.GetString() : null,
                    Path = c.TryGetProperty("path", out var p) ? p.GetString() : null,
                    HttpOnly = c.TryGetProperty("httpOnly", out var h) && h.GetBoolean(),
                    Secure = c.TryGetProperty("secure", out var s) && s.GetBoolean(),
                };
                if (c.TryGetProperty("expires", out var e) && e.ValueKind == JsonValueKind.Number)
                    cookie.Expires = (float)e.GetDouble();
                if (c.TryGetProperty("sameSite", out var ss) && ss.ValueKind == JsonValueKind.String)
                {
                    cookie.SameSite = ss.GetString() switch
                    {
                        "Lax" => SameSiteAttribute.Lax,
                        "Strict" => SameSiteAttribute.Strict,
                        "None" => SameSiteAttribute.None,
                        _ => null,
                    };
                }
                cookies.Add(cookie);
            }
            if (cookies.Count > 0)
                await page.Context.AddCookiesAsync(cookies);
        }

        if (root.TryGetProperty("origins", out var originsElement) && originsElement.ValueKind == JsonValueKind.Array)
        {
            var origins = new List<object>();
            foreach (var o in originsElement.EnumerateArray())
            {
                if (!o.TryGetProperty("localStorage", out var localStorageElement)) continue;
                var entries = new List<object>();
                foreach (var item in localStorageElement.EnumerateArray())
                {
                    entries.Add(new
                    {
                        name = item.GetProperty("name").GetString(),
                        value = item.GetProperty("value").GetString(),
                    });
                }
                if (entries.Count == 0) continue;
                origins.Add(new { origin = o.GetProperty("origin").GetString(), entries });
            }

            if (origins.Count > 0)
            {
                var payload = JsonSerializer.Serialize(origins);
                var script = $$"""
                    (() => {
                        const origins = {{payload}};
                        for (const o of origins) {
                            if (window.location.origin === o.origin) {
                                for (const e of o.entries) {
                                    window.localStorage.setItem(e.name, e.value);
                                }
                            }
                        }
                    })();
                    """;
                await page.AddInitScriptAsync(script);
            }
        }

        await page.GotoAsync($"{baseUrl}/");
        try
        {
            // ".employee-completion-dialog" too: a brand-new company's initial admin
            // (RequiresInitialSetup) is never shown ".app-shell" — MainLayout renders only the
            // blocking completion dialog. Either means the cached session applied successfully.
            await page.WaitForSelectorAsync(".app-shell, .employee-completion-dialog", new() { Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<BrowserNewContextOptions> LoginAndCaptureStorageStateAsync(IBrowser browser, string baseUrl, string personaEmail)
    {
        await _realLoginGate.WaitAsync();
        try
        {
            // A single transient real-Supabase hiccup (timeout, momentary rate-limit) shouldn't fail
            // this persona's ONE bootstrap login for the whole run — retry a few times with backoff
            // before giving up, matching the same defensive pattern already used for the dev-login
            // endpoint callers.
            //
            // Raised from 3 attempts / 2s-4s backoff to 5 attempts / 5s-25s backoff: even after
            // cutting maxParallelThreads (16 -> 6 -> 3) to relieve pressure on the shared Aspire app
            // itself, a couple of these still failed with the exact same symptom (RealFormLoginAsync's
            // OWN 45s per-attempt wait for the app shell timing out, 3 times in a row). Supabase Auth's
            // real password-grant endpoint is an external dependency this suite doesn't control and
            // isn't necessarily rate-limited purely by our own concurrent-request count — it can also
            // throttle by request rate over a rolling window. Fewer, more widely-spaced retries give a
            // transient throttle window more real time to clear than piling on more parallel app
            // capacity ever could.
            const int maxAttempts = 5;
            Exception? lastError = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await using var bootstrapContext = await browser.NewContextAsync();
                    var page = await bootstrapContext.NewPageAsync();
                    var login = new PageObjects.LoginPage(page, baseUrl);
                    await login.GoToAsync();
                    // Bypass the cache-aware LoginAsync here — this IS the code path that populates the
                    // cache, so going through LoginAsync would re-enter GetOrLoginAsync for the same
                    // in-flight Lazy and deadlock. RealFormLoginAsync always performs the real
                    // interactive form login.
                    await login.RealFormLoginAsync(personaEmail);

                    var storageState = await bootstrapContext.StorageStateAsync();
                    await page.CloseAsync();

                    return new BrowserNewContextOptions { StorageState = storageState };
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < maxAttempts)
                        await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                }
            }

            // No longer necessarily "real Supabase" — under E2E_TESTING=true, RealFormLoginAsync's
            // /api/login call hits FakeSupabaseAuthGateway's locally-signed token path instead (see
            // E2eFakeSupabaseJwt), which responds in single-digit milliseconds. A failure here now
            // almost always means the login page/app-shell itself didn't load in time under general
            // app load, not an auth-provider rate limit — named accordingly so it isn't misdiagnosed
            // as a Supabase issue again.
            throw new InvalidOperationException(
                $"E2E login for '{personaEmail}' failed after {maxAttempts} attempts (see inner exception — " +
                "likely the login page/app-shell not loading in time under load, not a Supabase auth failure).",
                lastError);
        }
        finally
        {
            _realLoginGate.Release();
        }
    }
}
