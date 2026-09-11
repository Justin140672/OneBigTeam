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
    // Laura Bennett — HR Administrator persona, same as RealSupabaseLoginFlowTests and
    // CircuitReconnectAfterCookieRemovalTests. Reused so this test can lean on the same known-working
    // "authenticated as Laura" sidebar/top-bar assertions those tests already establish.
    private const string Email = "laura.bennett@acme.example";

    private AppFixture _app = null!;
    private Microsoft.Playwright.IBrowserContext _context = null!;
    private Microsoft.Playwright.IPage _page = null!;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();
        _context = await _app.Browser.NewContextAsync();
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
        // Root-cause finding (revision 1): the original approach used
        // `_context.SetOfflineAsync(true)` to simulate a dropped SignalR connection, then waited for
        // the reconnect <dialog id="components-reconnect-modal"> to become visible. That waited the
        // full 15s timeout every run — i.e. the client never even noticed a drop. This matches
        // documented Playwright/CDP behaviour: BrowserContext.SetOfflineAsync blocks *new* outgoing
        // connections; it does not reliably tear down an *already-established* WebSocket such as
        // Blazor Server's SignalR transport.
        //
        // Root-cause finding (revision 2 and 3, both abandoned): both subsequent revisions used
        // Playwright's WebSocket routing API (Page.RouteWebSocketAsync + IWebSocketRoute.CloseAsync)
        // to close the *route object* Playwright hands back for an intercepted socket. This kept
        // failing with `TargetClosedException: The given key was not present in the dictionary` even
        // after adding an OnClose handler and a lock to null out a stale reference — first at ~838ms
        // (single occurrence), then at ~3.6s (two aggregated occurrences) after the OnClose fix
        // shifted the timing. The real problem was never "which reference is stale" — it's that this
        // app's login flow legitimately opens *more than two* "**/_blazor**" WebSocket connections
        // (an anonymous pre-login circuit, the authenticated post-login circuit, and then Blazor's own
        // reconnect-retry attempts once the drop is detected, all matching the same route glob), and
        // each of those transitions is a race between Playwright's driver-side object registry
        // removing an entry for a socket that closed on its own (real navigation, server-side
        // handshake teardown, a reconnect retry superseding an earlier one) and this test code trying
        // to call a method on that same registry entry. No amount of reference-nulling on our side can
        // fully close that race, because the registry entry can disappear on Playwright's dispatch
        // thread at any point between our lock-protected read and the awaited CloseAsync() call
        // actually reaching the driver. In short: manually tracking "the current live route object"
        // via IWebSocketRoute is fundamentally too fragile for an app with this many WebSocket
        // transitions in one test — the fix needed to stop going through Playwright's route registry
        // for this at all.
        //
        // Fix (this revision): capture and close the real, unambiguous WebSocket object living in the
        // page's own JavaScript, never Playwright's automation-layer route object. We inject an init
        // script (Page.AddInitScriptAsync) that monkey-patches `window.WebSocket` *before* any page
        // script runs, recording every WebSocket instance ever constructed into `window.__wsInstances`.
        // Because AddInitScriptAsync re-applies the script on every new document (per Playwright docs),
        // this survives the forced navigation back to /login later without any extra wiring. To force
        // the drop, we run `page.EvaluateAsync` to find the most recent OPEN socket whose url contains
        // "_blazor" and call `.close()` on it directly in the browser — a genuine, native
        // `WebSocket.prototype.close()` call that unconditionally dispatches a real client-side
        // `onclose` event, which is exactly what blazor.server.js's default reconnection handler reacts
        // to. There is no Playwright driver-side object/guid involved anywhere in this step, so there
        // is nothing for a `TargetClosedException`/`KeyNotFoundException` race to attach to — the only
        // failure mode left is "no matching open socket found", which we assert on explicitly.
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

        // Establish an initial, genuinely authenticated circuit.
        await login.GoToAsync();
        await login.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        var userInfo = _page.Locator(".top-bar-user-info");
        await userInfo.WaitForAsync(new() { Timeout = 10_000 });
        Assert.Contains("Laura Bennett", await userInfo.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        // Invalidate the live circuit's auth state without navigating away: remove the session
        // cookie (mirrors logout/expiry in another tab), then force the existing SignalR connection
        // to actually drop and reconnect — which is what triggers CircuitHost to call
        // SetAuthenticationState again with the now-cookie-less request's HttpContext.User, driving
        // AppSessionAuthStateProvider.ApplyState's Clear()/Invalidated path.
        //
        // Synchronise on the reconnect <dialog id="components-reconnect-modal"> itself (a real,
        // standard HTML <dialog> shown via showModal()/close() — see ReconnectModal.razor.js), which
        // is this app's own authoritative, observable signal for "the client has detected the drop"
        // and "the client believes it has recovered", instead of guessing fixed durations.
        await _context.ClearCookiesAsync();

        // Find the most recent OPEN Blazor SignalR socket recorded by the init-script patch above, and
        // close it directly from within the page's own JS context. This is a plain
        // `WebSocket.prototype.close()` call on the real object the browser is holding — not a
        // Playwright automation-layer handle — so there is no driver-side registry entry that can race
        // out from under us the way IWebSocketRoute did.
        var closedRealSocket = await _page.EvaluateAsync<bool>(@"
() => {
  const sockets = (window.__wsInstances || []).filter(ws =>
    ws.url && ws.url.includes('_blazor') && ws.readyState === WebSocket.OPEN);
  if (sockets.length === 0) return false;
  sockets[sockets.length - 1].close();
  return true;
}
");

        // The initial page load must have opened the Blazor WebSocket already, so we should always
        // find at least one open, matching socket to close — if this is false, either the init-script
        // patch never captured a `_blazor` WebSocket (a real bug worth failing loudly on) or the app
        // had already torn its own socket down before we got here (which would itself mean the client
        // is already mid-drop, so this assertion failing would be a genuine signal worth investigating
        // rather than something to silently swallow).
        Assert.True(closedRealSocket, "Expected to find and close an open Blazor SignalR WebSocket.");

        // Wait for the client to actually notice the dropped connection (the reconnect dialog opens).
        // Closing the actual WebSocket object guarantees blazor.server.js's onclose handler fires
        // immediately, so this is not a guessing game.
        var reconnectModal = _page.Locator("#components-reconnect-modal");
        await reconnectModal.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Visible, Timeout = 15_000 });

        // No interception is in place for subsequent connections — we never routed traffic through
        // Playwright at all in this revision, only tagged sockets via the init script — so Blazor's
        // own built-in reconnect retries hit the real server directly and succeed (or fail) exactly as
        // they would outside of Playwright.

        // Wait for the client to consider itself recovered (dialog closes, having invoked
        // Blazor.reconnect()/resumeCircuit() successfully — possibly after several backoff retries
        // per ReconnectModal.razor.js's scheduleAutoRecover) before assuming SetAuthenticationState
        // has been re-invoked on the server for this circuit.
        await reconnectModal.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden, Timeout = 30_000 });

        // With the fix in place, the circuit is now expected to have forced a real browser navigation
        // to /login (Routes.razor's <NotAuthorized> branch, forceLoad: true) rather than continuing to
        // present the old, now-invalidated circuit. Give the app a moment to complete that redirect;
        // if it hasn't happened on its own (e.g. because no in-app navigation has occurred yet to
        // trigger the NotAuthorized branch), drive one explicitly the same way
        // CircuitReconnectAfterCookieRemovalTests does, via the sidebar.
        if (!_page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
        {
            var sidebar = new SidebarPage(_page);
            try
            {
                await sidebar.ClickGroupedMenuItemAsync("People and users", "Employees");
            }
            catch
            {
                // A thrown navigation/locator failure here is itself acceptable evidence the old
                // circuit is no longer usable — fall through to the wait below.
            }
        }

        await _page.WaitForURLAsync(new Regex("/login"), new() { Timeout = 20_000 });

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

        // Confirm the circuit is genuinely live and authenticated (not a stale/anonymous shell) by
        // driving a real circuit-scoped API call, same technique as
        // RealSupabaseLoginFlowTests.RealFormLogin_ThenCircuitDrivenApiCall_IsAuthenticated_NotTreatedAsAnonymous.
        var sidebarAfterReLogin = new SidebarPage(_page);
        await sidebarAfterReLogin.ClickGroupedMenuItemAsync("People and users", "Employees");
        await _page.WaitForURLAsync(new Regex("/employees"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/login", _page.Url);

        var grid = _page.Locator(".e-grid, [data-testid='employee-list']").First;
        await grid.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await grid.IsVisibleAsync());
    }
}
