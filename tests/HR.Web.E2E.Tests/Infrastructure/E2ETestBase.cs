using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for all E2E tests. Manages browser context and page lifecycle. When the fixture
/// carries a pre-authenticated storageState (the 4 role-fixed fixtures), every test's context starts
/// already logged in — see IPersonaFixture.AuthenticatedContextOptions and LoginPage.LoginAsync,
/// which is a no-op when the requested persona already matches. Only CrossUserFixture (persona
/// switching mid-test) still pays the full per-test UI login and needs the teardown delay below.
/// </summary>
public abstract class E2ETestBase(IPersonaFixture fixture) : IAsyncLifetime
{
    protected readonly IPersonaFixture _fixture = fixture;
    protected          IBrowserContext _context = null!;
    protected          IPage           _page    = null!;

    // Rolling diagnostic buffers — dumped alongside the screenshot in DisposeAsync when the
    // authenticated shell never rendered, so a failing run says WHY (an api/me 401/403/500, a
    // JS error) without a re-run.
    private readonly List<string> _consoleErrors = new();
    private readonly List<string> _failedResponses = new();

    public virtual async Task InitializeAsync()
    {
        _context = _fixture.AuthenticatedContextOptions is { } options
            ? await _fixture.Browser.NewContextAsync(options)
            : await _fixture.Browser.NewContextAsync();
        _page = await _context.NewPageAsync();

        _page.Console += (_, msg) =>
        {
            if (msg.Type is "error" or "warning")
                _consoleErrors.Add($"[{msg.Type}] {msg.Text}");
        };
        _page.Response += (_, res) =>
        {
            if (res.Status >= 400)
                _failedResponses.Add($"{res.Status} {res.Request.Method} {res.Url}");
        };

        // Sensible defaults: actions wait up to 30 s, navigation up to 30 s.
        // Individual waits can still override with an explicit Timeout option.
        // Raised from 15s — under a full-suite run the shared Aspire-hosted app gets busy
        // enough that server round-trips (e.g. task completion) occasionally exceed 15s,
        // which surfaced as flaky TargetClosedException failures across unrelated tests.
        _page.SetDefaultTimeout(30_000);
        _page.SetDefaultNavigationTimeout(30_000);
    }

    public virtual async Task DisposeAsync()
    {
        // Diagnostic capture: if the authenticated shell never rendered, something upstream of the
        // test's own assertions went wrong (login/session/circuit) — dump a screenshot + the live
        // DOM + console so a failing run can be diagnosed without re-running interactively. Written
        // to bin/.../diag/. Best-effort; never let it affect teardown.
        try
        {
            if (!await _page.Locator(".app-shell").IsVisibleAsync())
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "diag");
                Directory.CreateDirectory(dir);
                var stamp = $"{DateTime.UtcNow:HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}";
                await _page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{stamp}.png"), FullPage = true });
                var failed = _failedResponses.Count > 0 ? string.Join("\n", _failedResponses) : "(none)";
                var console = _consoleErrors.Count > 0 ? string.Join("\n", _consoleErrors) : "(none)";
                await File.WriteAllTextAsync(
                    Path.Combine(dir, $"{stamp}.html"),
                    $"URL: {_page.Url}\n\n=== FAILED RESPONSES (>=400) ===\n{failed}\n\n=== CONSOLE ERRORS/WARNINGS ===\n{console}\n\n=== DOM ===\n{await _page.ContentAsync()}");
            }
        }
        catch { /* diagnostics only */ }

        // Navigate away before closing so that any Blazor error boundary or faulted circuit
        // is torn down cleanly on the server side, preventing its error UI from bleeding into
        // the next test's fresh context via a reconnecting circuit.
        try { await _page.GotoAsync("about:blank"); } catch { /* ignore navigation errors on teardown */ }

        await _context.DisposeAsync();

        if (_fixture.RequiresFullTeardownDelay)
        {
            // CrossUser only: let the Blazor Server circuit disconnect before the next test
            // switches dev-auth persona. 1 500 ms is not enough on a warm app under load — the
            // lingering circuit can still be making API calls when the next test navigates and
            // switches auth persona. Role-fixed collections never switch persona within their own
            // tests (an outlier persona login re-authenticates cleanly via cookie clearing instead
            // of a live persona switch), so they skip this delay entirely.
            await Task.Delay(3_000);
        }
    }

    /// <summary>
    /// Polls the current page URL until it no longer contains <paramref name="urlFragment"/>, or
    /// the timeout elapses (whichever first). Used for asserting an authorization redirect actually
    /// completed: the redirect itself is typically a client-side Blazor NavigateTo fired from
    /// OnBeforeLoadAsync, not a full page navigation, so page.WaitForLoadStateAsync(NetworkIdle) is
    /// not a reliable signal — the initial GET's network can go idle well before the subsequent
    /// client-side redirect fires, especially under heavy parallel load on shared seeded personas.
    /// Polling the URL directly is unaffected by that timing gap. Does not throw on timeout — the
    /// caller is expected to assert on the resulting Url itself afterwards.
    /// </summary>
    protected async Task WaitForUrlToStopContainingAsync(string urlFragment, int timeoutMs = 20_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (_page.Url.Contains(urlFragment) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
    }

    /// <summary>
    /// Polls the current page URL until <paramref name="predicate"/> is satisfied, or the timeout
    /// elapses. Same rationale as <see cref="WaitForUrlToStopContainingAsync"/> (a client-side
    /// NavigateTo redirect fired from a route guard once the interactive circuit connects is not
    /// visible to WaitForLoadStateAsync/NetworkIdle) but for redirect targets that can't be
    /// expressed as a plain "no longer contains X" — e.g. a redirect to the user's own profile,
    /// whose URL still contains the list route as a path prefix. Does not throw on timeout: the
    /// caller asserts on the resulting <see cref="IPage.Url"/> afterwards for a clear message.
    /// </summary>
    protected async Task WaitForUrlAsync(Func<string, bool> predicate, int timeoutMs = 20_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate(_page.Url) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
    }
}

/// <summary>
/// Base for the ~139 role-fixed test classes (HrAdmin/Manager/Recruiter/Employee personas). Declares
/// IClassFixture&lt;TFixture&gt; itself so every derived test class picks up xUnit's per-class fixture
/// injection automatically — a plain "E2ETestBase(fixture)" base without this marker on the class
/// hierarchy leaves xUnit unable to resolve the constructor argument (surfaces as analyzer warning
/// xUnit1041, and at runtime as a fixture that's never actually created/injected). CrossUser test
/// classes intentionally do NOT use this base — they use ICollectionFixture&lt;CrossUserFixture&gt; via
/// the "CrossUser" [Collection] attribute instead, which doesn't need an IClassFixture marker.
/// </summary>
public abstract class RoleE2ETestBase<TFixture>(TFixture fixture) : E2ETestBase(fixture), IClassFixture<TFixture>
    where TFixture : class, IPersonaFixture;
