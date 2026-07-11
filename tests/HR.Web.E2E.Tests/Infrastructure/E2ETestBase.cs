using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for all E2E tests. Manages browser context and page lifecycle, and
/// inserts a short teardown delay after each test so that the Blazor Server SignalR
/// circuit has time to detect the disconnection before the next test switches the
/// dev-auth persona. Without the delay, a lingering circuit from the just-finished
/// test can make API calls authenticated as the next test's persona.
/// </summary>
public abstract class E2ETestBase(AppFixture fixture) : IAsyncLifetime
{
    protected readonly AppFixture    _fixture = fixture;
    protected          IBrowserContext _context = null!;
    protected          IPage           _page    = null!;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();

        // Sensible defaults: actions wait up to 30 s, navigation up to 30 s.
        // Individual waits can still override with an explicit Timeout option.
        // Raised from 15s — under a full-suite run the shared Aspire-hosted app gets busy
        // enough that server round-trips (e.g. task completion) occasionally exceed 15s,
        // which surfaced as flaky TargetClosedException failures across unrelated tests.
        _page.SetDefaultTimeout(30_000);
        _page.SetDefaultNavigationTimeout(30_000);
    }

    public async Task DisposeAsync()
    {
        // Navigate away before closing so that any Blazor error boundary or faulted circuit
        // is torn down cleanly on the server side, preventing its error UI from bleeding into
        // the next test's fresh context via a reconnecting circuit.
        try { await _page.GotoAsync("about:blank"); } catch { /* ignore navigation errors on teardown */ }

        await _context.DisposeAsync();
        // Let the Blazor Server circuit disconnect before the next test switches personas.
        // 1 500 ms is not enough on a warm app under load — the lingering circuit can
        // still be making API calls when the next test navigates and switches auth persona.
        await Task.Delay(3_000);
    }
}
