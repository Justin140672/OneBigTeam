using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 11 regression: browser coverage for "cookie removal/logout followed by reconnection of an
/// existing (still-open) Blazor Server circuit".
///
/// Context: AppSessionAuthStateProvider.SetAuthenticationState (see HR.Web/Services/
/// AppSessionAuthStateProvider.cs) is invoked by Blazor Server's CircuitHost both at circuit creation
/// AND on every reconnect of an already-open circuit (see
/// https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Server/src/ComponentHub.cs#L211).
/// The P1 fix ensures a reconnect that arrives without a valid session cookie (logout, or cookie
/// expiry) clears the circuit's retained token rather than continuing to use the old one. This test
/// approximates that scenario end-to-end: log in normally (establishing a live circuit with a valid
/// token), remove the session cookie from the browser context (simulating logout/expiry without
/// tearing down the SignalR connection), force a SignalR reconnect by dropping the network briefly
/// and restoring it, then assert the circuit no longer behaves as the previously authenticated user
/// (a subsequent in-app navigation/API call must not succeed as Laura Bennett; the app must treat the
/// circuit as unauthenticated, e.g. by redirecting to /login or showing an unauthenticated state).
///
/// Known risk (flagged, not resolved here): Blazor Server's SignalR client may, depending on
/// circuit-disconnect timing/hosting configuration, tear down and recreate the circuit entirely
/// (a fresh circuit creation, not a "reconnect" of the same one) rather than performing the
/// server-side reconnect path this test intends to exercise. Both outcomes are safe from a security
/// standpoint (a fresh circuit calls SetAuthenticationState once with the now-anonymous principal;
/// a genuine reconnect calls it again on the existing circuit with the same result), but only the
/// reconnect path is the literal scenario Ticket 11 describes. A maintainer running this for real
/// should confirm via server logs/circuit IDs which path Playwright's short network blip actually
/// triggers, and adjust the network-interruption technique (e.g. Playwright's CDP-level connection
/// drop, or a longer offline window tuned to the app's configured DisconnectedCircuitRetentionPeriod)
/// if it turns out to always produce a fresh circuit instead.
/// </summary>
public sealed class CircuitReconnectAfterCookieRemovalTests : IAsyncLifetime
{
    private const string Email = "laura.bennett@acme.example";

    private AppFixture _app = null!;
    private Microsoft.Playwright.IBrowserContext _context = null!;
    private Microsoft.Playwright.IPage _page = null!;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();
        _context = await _app.Browser.NewContextAsync(E2eBrowserContextOptions.Create());
        await _context.AddInitScriptAsync(TrackWebSocketsScript);
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
    public async Task ReconnectWithoutSessionCookie_DoesNotResumeAsPreviouslyAuthenticatedUser()
    {
        var login = new LoginPage(_page, _app.WebBaseUrl);

        await login.GoToAsync();
        await login.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        var userInfo = _page.Locator(".top-bar-user-info");
        await userInfo.WaitForAsync(new() { Timeout = 10_000 });
        Assert.Contains("Laura Bennett", await userInfo.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await _context.ClearCookiesAsync();

        var closedSockets = await _page.EvaluateAsync<int>(
            "() => { let n = 0; for (const s of (window.__e2eSockets || [])) { " +
            "if (s.readyState === WebSocket.OPEN) { s.close(4000, 'e2e forced drop'); n++; } } return n; }");
        Assert.True(closedSockets > 0,
            "Expected at least one open Blazor WebSocket to sever — the reconnect path was not exercised.");

        // Drive an in-app navigation (not a full page reload/GotoAsync) so this exercises whatever
        // circuit is now live — if the fix worked, this must NOT succeed as Laura Bennett. Depending
        // on how Blazor's client reacts to a failed/degraded reconnect, this may render as: the
        // sidebar navigation itself failing, a redirect to /login, or an app-level "session expired"
        // prompt — any of those are acceptable evidence of "not resumed as the previous user"; what
        // is NOT acceptable is the employees grid loading successfully while still showing Laura
        // Bennett as the signed-in user.
        var sidebar = new SidebarPage(_page);
        try
        {
            await sidebar.ClickGroupedMenuItemAsync("People and users", "Employees");
        }
        catch
        {
        }

        var topBarUserInfo = _page.Locator(".top-bar-user-info");
        var resumedAsLaura = true;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                resumedAsLaura = false;
                break;
            }

            var texts = await topBarUserInfo.AllInnerTextsAsync();
            if (!texts.Any(t => t.Contains("Laura Bennett", StringComparison.OrdinalIgnoreCase)))
            {
                resumedAsLaura = false;
                break;
            }

            await _page.WaitForTimeoutAsync(250);
        }

        Assert.False(resumedAsLaura,
            "Circuit resumed showing Laura Bennett as authenticated after its session cookie was removed and the connection reconnected — the stale token was not cleared.");
    }

    private const string TrackWebSocketsScript = """
        (() => {
            if (window.__e2eSocketsInstalled) return;
            window.__e2eSocketsInstalled = true;
            window.__e2eSockets = [];
            const Native = window.WebSocket;
            class TrackedWebSocket extends Native {
                constructor(...args) {
                    super(...args);
                    window.__e2eSockets.push(this);
                }
            }
            window.WebSocket = TrackedWebSocket;
        })();
        """;
}
