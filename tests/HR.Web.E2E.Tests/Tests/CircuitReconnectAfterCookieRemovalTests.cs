using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 11 regression: browser coverage for "cookie removal/logout followed by reconnection of an
/// existing (still-open) Blazor Server circuit".
///
/// NOT RUN / UNVERIFIED — written per the Ticket 11 requirement to add this coverage, but this
/// environment cannot run Playwright/E2E tests or start a dev server (project policy). This file has
/// never been executed. Before relying on it, a maintainer with a runnable environment must execute
/// it, confirm it actually exercises a genuine SignalR RECONNECT (not merely a fresh negotiate from a
/// full page reload — see remarks below on why that distinction is easy to get wrong here), and fix
/// up any selector/timing issues that only show up under a live browser.
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
    // Laura Bennett — HR Administrator persona, same as RealSupabaseLoginFlowTests. Chosen so this
    // test can reuse an existing, known-working sidebar navigation assertion for "still authenticated
    // as Laura" vs. "no longer authenticated".
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

        // Remove the session cookie from the browser context without navigating away — this leaves
        // the open SignalR/circuit connection intact for now, mirroring "logout in another tab" or
        // "cookie expired" while this tab's circuit is still alive.
        await _context.ClearCookiesAsync();

        // Force the existing SignalR connection to drop and (attempt to) reconnect by briefly taking
        // the browser context offline and then restoring connectivity. On restore, Blazor Server's
        // client attempts to resume the same circuit, which is what triggers CircuitHost to call
        // SetAuthenticationState again with the now-cookie-less request's HttpContext.User.
        await _context.SetOfflineAsync(true);
        await _page.WaitForTimeoutAsync(2_000);
        await _context.SetOfflineAsync(false);

        // Give the client time to detect the drop and complete (or fail) its reconnect attempt.
        await _page.WaitForTimeoutAsync(5_000);

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
            // A thrown navigation/locator failure here is itself acceptable evidence that the circuit
            // is no longer in a normally-authenticated, fully-functional state — fall through to the
            // assertions below, which are the actual source of truth for this test.
        }

        var stillOnLoginOrUnauthenticated =
            _page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase);

        if (!stillOnLoginOrUnauthenticated)
        {
            // If the app didn't redirect to /login, it must not be showing Laura Bennett as the
            // authenticated user in the top bar any more (a fresh, correctly-anonymous circuit's
            // top bar should not resolve to her display name, since that requires a valid /api/me
            // call which now has no bearer token to send).
            var topBarStillShowsLaura = await _page.Locator(".top-bar-user-info")
                .GetByText("Laura Bennett", new() { Exact = false })
                .CountAsync() > 0;

            Assert.False(topBarStillShowsLaura,
                "Circuit resumed showing Laura Bennett as authenticated after its session cookie was removed and the connection reconnected — the stale token was not cleared.");
        }
    }
}
