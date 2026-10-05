using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NOT RUN / UNVERIFIED — written per Ticket 12's regression-test requirement, but this environment
/// cannot run Playwright/E2E tests or start a dev server (project policy). This file has never been
/// executed. Before relying on it, a maintainer with a runnable environment must execute it, confirm
/// the assertions hold under a live browser, and fix up any selector/timing issues.
///
/// Context: Ticket 12 fixed a P1 bug in CircuitSessionState/AppSessionAuthStateProvider.ApplyState
/// (see HR.Web/Services/CircuitSessionState.cs and HR.Web/Services/AppSessionAuthStateProvider.cs)
/// where an already-invalidated circuit (one that had seen an anonymous or different-identity
/// reconnect after being authenticated) could wrongly accept a brand-new token on a LATER
/// SetAuthenticationState call, because "is this a first seed" was inferred from AccessToken being
/// null — which Clear() also produces. The fix adds an explicit CircuitAuthStatus
/// (Uninitialized/Authenticated/Invalidated) that is sticky once Invalidated: no later call on that
/// same circuit instance can ever authenticate again. Routes.razor's &lt;NotAuthorized&gt; branch now
/// also navigates with forceLoad: true, so a fail-closed circuit is abandoned in favour of a genuinely
/// fresh one (new SignalR connection, new DI scope, new CircuitSessionState) rather than risking any
/// further reuse of the same (poisoned) circuit instance.
///
/// This test approximates that scenario end-to-end, building on the existing
/// CircuitReconnectAfterCookieRemovalTests pattern: log in as Laura Bennett, invalidate the live
/// circuit's auth state by clearing the session cookie and forcing a reconnect (mirroring "logout in
/// another tab" or cookie expiry while this tab's circuit is still alive), then attempt a fresh
/// re-login. Because the app is expected to have already forced a real browser navigation (forceLoad)
/// away from the poisoned circuit once it noticed it was unauthenticated, the subsequent re-login
/// must succeed cleanly on a brand-new circuit rather than silently continuing on/attaching to the
/// invalidated one.
/// </summary>
public sealed class CircuitInvalidationBlocksReauthenticationTests : IAsyncLifetime
{
    private const string Email = "laura.bennett@acme.example";

    private AppFixture _app = null!;
    private Microsoft.Playwright.IBrowserContext _context = null!;
    private Microsoft.Playwright.IPage _page = null!;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();
        _context = await _app.Browser.NewContextAsync(E2eBrowserContextOptions.Create());
        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(30_000);
        _page.SetDefaultNavigationTimeout(30_000);
    }

    public async Task DisposeAsync()
    {
        try { await _page.GotoAsync("about:blank"); } catch { /* ignore navigation errors on teardown */ }
        await _context.DisposeAsync();
        await SharedAppFixture.ReleaseAsync();
    }

    [Fact]
    public async Task ReLoginAfterCircuitInvalidation_EstablishesFreshCircuit_AndAuthenticatesCleanly()
    {
        await _page.AddInitScriptAsync(@"
(() => {
  const NativeWebSocket = window.WebSocket;
  window.__wsInstances = [];
  function PatchedWebSocket(url, protocols) {
    const ws = (protocols === undefined) ? new NativeWebSocket(url) : new NativeWebSocket(url, protocols);
    window.__wsInstances.push(ws);
    return ws;
  }
  PatchedWebSocket.prototype = NativeWebSocket.prototype;
  PatchedWebSocket.CONNECTING = NativeWebSocket.CONNECTING;
  PatchedWebSocket.OPEN = NativeWebSocket.OPEN;
  PatchedWebSocket.CLOSING = NativeWebSocket.CLOSING;
  PatchedWebSocket.CLOSED = NativeWebSocket.CLOSED;
  window.WebSocket = PatchedWebSocket;
})();
");

        var login = new LoginPage(_page, _app.WebBaseUrl);

        await login.GoToAsync();
        await login.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        var userInfo = _page.Locator(".top-bar-user-info");
        await userInfo.WaitForAsync(new() { Timeout = 10_000 });
        Assert.Contains("Laura Bennett", await userInfo.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await _context.ClearCookiesAsync();

        var closedRealSocket = await _page.EvaluateAsync<bool>(@"
() => {
  const sockets = (window.__wsInstances || []).filter(ws =>
    ws.url && ws.url.includes('_blazor') && ws.readyState === WebSocket.OPEN);
  if (sockets.length === 0) return false;
  sockets[sockets.length - 1].close();
  return true;
}
");

        Assert.True(closedRealSocket, "Expected to find and close an open Blazor SignalR WebSocket.");

        await _page.WaitForURLAsync(new Regex("/login"), new() { Timeout = 45_000 });

        // Re-login on what must now be a genuinely fresh circuit (a full browser navigation to
        // /login, per forceLoad: true, tears down the old SignalR connection and DI scope — so the
        // new CircuitSessionState created for the next circuit starts back at Uninitialized, not
        // sticky-Invalidated). This is the crux of the regression check: before the Ticket 12 fix,
        // there was no guarantee the app would ever abandon the poisoned circuit at all, so a
        // "reconnect with a different identity next" sequence could silently keep running on it.
        var reLogin = new LoginPage(_page, _app.WebBaseUrl);
        await reLogin.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/hr", _page.Url);

        var userInfoAfterReLogin = _page.Locator(".top-bar-user-info");
        await userInfoAfterReLogin.WaitForAsync(new() { Timeout = 10_000 });
        Assert.Contains("Laura Bennett", await userInfoAfterReLogin.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        var sidebarAfterReLogin = new SidebarPage(_page);
        await sidebarAfterReLogin.ClickGroupedMenuItemAsync("People and users", "Employees");
        await _page.WaitForURLAsync(new Regex("/employees"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/login", _page.Url);

        var grid = _page.Locator(".e-grid, [data-testid='employee-list']").First;
        await grid.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await grid.IsVisibleAsync());
    }
}
