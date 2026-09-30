using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

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

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new();

    private static readonly TimeSpan _recentFailureCooldown = TimeSpan.FromSeconds(120);
    private static readonly ConcurrentDictionary<string, (DateTime FailedAtUtc, ExceptionDispatchInfo Failure)> _recentFailures = new();

    private static bool TryGetRecentFailure(string personaEmail, out ExceptionDispatchInfo failure)
    {
        if (_recentFailures.TryGetValue(personaEmail, out var recent) &&
            DateTime.UtcNow - recent.FailedAtUtc < _recentFailureCooldown)
        {
            failure = recent.Failure;
            return true;
        }

        failure = null!;
        return false;
    }

    public static Task<BrowserNewContextOptions> GetOrLoginAsync(AppFixture app, string personaEmail) =>
        GetOrLoginAsync(app.Browser, app.WebBaseUrl, personaEmail);

    public static async Task<BrowserNewContextOptions> GetOrLoginAsync(IBrowser browser, string baseUrl, string personaEmail)
    {
        var (options, _) = await GetOrLoginWithEntryAsync(browser, baseUrl, personaEmail);
        return options;
    }

    public static async Task<(BrowserNewContextOptions Options, object Entry)> GetOrLoginWithEntryForCallerAsync(
        IBrowser browser, string baseUrl, string personaEmail)
    {
        var (options, entry) = await GetOrLoginWithEntryAsync(browser, baseUrl, personaEmail);
        return (options, entry);
    }

    private static async Task<(BrowserNewContextOptions Options, Lazy<Task<BrowserNewContextOptions>> Entry)> GetOrLoginWithEntryAsync(
        IBrowser browser, string baseUrl, string personaEmail)
    {
        var alreadyCached = _cache.ContainsKey(personaEmail);
        if (!alreadyCached && TryGetRecentFailure(personaEmail, out var recentFailure))
        {
            E2eDiag.Log("PersonaLoginCache", $"{personaEmail}: bootstrap login failed recently; rethrowing without a new attempt");
            recentFailure.Throw();
        }

        var entry = _cache.GetOrAdd(
            personaEmail,
            email => new Lazy<Task<BrowserNewContextOptions>>(
                () => LoginAndCaptureStorageStateAsync(browser, baseUrl, email),
                LazyThreadSafetyMode.ExecutionAndPublication));
        E2eDiag.Log("PersonaLoginCache", $"{personaEmail}: cache {(alreadyCached ? "HIT (awaiting existing entry, may already be settled or in-flight)" : "MISS (this call will trigger LoginAndCaptureStorageStateAsync)")}");

        try
        {
            using var _ = E2eDiag.Time("PersonaLoginCache", $"{personaEmail}: await cache entry");
            return (await entry.Value, entry);
        }
        catch
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<BrowserNewContextOptions>>>>)_cache)
                .Remove(new KeyValuePair<string, Lazy<Task<BrowserNewContextOptions>>>(personaEmail, entry));
            throw;
        }
    }

    public static async Task<BrowserNewContextOptions> InvalidateAndRefreshAsync(
        IBrowser browser, string baseUrl, string personaEmail, object staleEntry)
    {
        var gate = _refreshGates.GetOrAdd(personaEmail, _ => new SemaphoreSlim(1, 1));
        var gateWait = E2eDiag.Time("PersonaLoginCache", $"{personaEmail}: refresh gate wait (contended: {gate.CurrentCount == 0})");
        await gate.WaitAsync();
        gateWait.Dispose();
        try
        {
            if (TryGetRecentFailure(personaEmail, out var recentFailure))
            {
                E2eDiag.Log("PersonaLoginCache",
                    $"{personaEmail}: short-circuiting — a bootstrap login failed within the last {_recentFailureCooldown.TotalSeconds:F0}s; " +
                    "rethrowing that failure instead of repeating a doomed attempt");
                recentFailure.Throw();
            }

            if (staleEntry is Lazy<Task<BrowserNewContextOptions>> typedStaleEntry)
            {
                ((ICollection<KeyValuePair<string, Lazy<Task<BrowserNewContextOptions>>>>)_cache)
                    .Remove(new KeyValuePair<string, Lazy<Task<BrowserNewContextOptions>>>(personaEmail, typedStaleEntry));
            }

            var (options, _) = await GetOrLoginWithEntryAsync(browser, baseUrl, personaEmail);
            return options;
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task PublishAsync(string personaEmail, IPage page)
    {
        var storageState = await page.Context.StorageStateAsync();
        var options = E2eBrowserContextOptions.Create(storageState);
        _cache[personaEmail] = new Lazy<Task<BrowserNewContextOptions>>(
            () => Task.FromResult(options), LazyThreadSafetyMode.ExecutionAndPublication);
    }

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

        var gotoTimer = E2eDiag.Time("PersonaLoginCache", "TryApplyStorageStateAsync: GotoAsync(/)");
        await page.GotoAsync($"{baseUrl}/");
        gotoTimer.Dispose();
        var shellWaitTimer = E2eDiag.Time("PersonaLoginCache", "TryApplyStorageStateAsync: wait for .app-shell/.employee-completion-dialog (10s budget)");
        try
        {
            await page.WaitForSelectorAsync(".app-shell, .employee-completion-dialog", new() { Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            E2eDiag.Log("PersonaLoginCache", $"TryApplyStorageStateAsync: 10s app-shell wait TIMED OUT at url={page.Url} — treating cached session as stale (may be a false negative under load, not an actually-stale session)");
            return false;
        }
        finally
        {
            shellWaitTimer.Dispose();
        }
    }

    private static async Task<BrowserNewContextOptions> LoginAndCaptureStorageStateAsync(IBrowser browser, string baseUrl, string personaEmail)
    {
        E2eDiag.Log("PersonaLoginCache", $"{personaEmail}: waiting on real-login gate (available slots: {_realLoginGate.CurrentCount}/6)");
        var gateWait = E2eDiag.Time("PersonaLoginCache", $"{personaEmail}: real-login gate wait");
        await _realLoginGate.WaitAsync();
        gateWait.Dispose();
        try
        {
            const int maxAttempts = 3;
            Exception? lastError = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var attemptTimer = E2eDiag.Time("PersonaLoginCache", $"{personaEmail}: real login attempt {attempt}/{maxAttempts}");
                try
                {
                    await using var bootstrapContext = await browser.NewContextAsync(E2eBrowserContextOptions.Create());
                    var page = await bootstrapContext.NewPageAsync();
                    var login = new PageObjects.LoginPage(page, baseUrl);
                    await login.GoToAsync();
                    await login.RealFormLoginAsync(personaEmail);

                    var storageState = await bootstrapContext.StorageStateAsync();
                    await page.CloseAsync();

                    _recentFailures.TryRemove(personaEmail, out _);
                    return E2eBrowserContextOptions.Create(storageState);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    E2eDiag.Log("PersonaLoginCache", $"{personaEmail}: attempt {attempt}/{maxAttempts} FAILED — {ex.GetType().Name}: {ex.Message}");
                    if (attempt < maxAttempts)
                        await Task.Delay(TimeSpan.FromSeconds(3 * attempt));
                }
            }

            var failure = new InvalidOperationException(
                $"E2E login for '{personaEmail}' failed after {maxAttempts} attempts (see inner exception for the page state at the last timeout).",
                lastError);
            _recentFailures[personaEmail] = (DateTime.UtcNow, ExceptionDispatchInfo.Capture(failure));
            throw failure;
        }
        finally
        {
            _realLoginGate.Release();
        }
    }
}
