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
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        // Let the Blazor Server circuit disconnect before the next test switches personas.
        await Task.Delay(1_500);
    }
}
