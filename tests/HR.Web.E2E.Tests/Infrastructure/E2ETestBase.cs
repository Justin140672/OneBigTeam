using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public abstract class E2ETestBase(IPersonaFixture fixture) : IAsyncLifetime
{
    protected readonly IPersonaFixture _fixture = fixture;
    protected          IBrowserContext _context = null!;
    protected          IPage           _page    = null!;

    private readonly List<string> _consoleErrors = new();
    private readonly List<string> _failedResponses = new();

    public virtual async Task InitializeAsync()
    {
        _context = _fixture.AuthenticatedContextOptions is { } options
            ? await _fixture.Browser.NewContextAsync(options)
            : await _fixture.Browser.NewContextAsync(E2eBrowserContextOptions.Create());
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

        _page.SetDefaultTimeout(30_000);
        _page.SetDefaultNavigationTimeout(30_000);
    }

    public virtual async Task DisposeAsync()
    {
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

        try { await _page.GotoAsync("about:blank"); } catch { /* ignore navigation errors on teardown */ }

        await _context.DisposeAsync();

        if (_fixture.RequiresFullTeardownDelay)
        {
            await Task.Delay(3_000);
        }
    }

    protected async Task WaitForUrlToStopContainingAsync(string urlFragment, int timeoutMs = 45_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (_page.Url.Contains(urlFragment) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
    }

    protected async Task WaitForUrlAsync(Func<string, bool> predicate, int timeoutMs = 45_000)
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
