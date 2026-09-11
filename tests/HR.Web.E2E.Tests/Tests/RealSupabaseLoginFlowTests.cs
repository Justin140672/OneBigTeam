using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Deliberate canary for the real Supabase Auth password-grant login path.
///
/// Context (2026-08-17 investigation): almost every other test in this suite logs in via
/// <see cref="LoginPage.LoginAsync"/>, which prefers <see cref="PersonaLoginCache"/>'s cached
/// Playwright storageState over driving the login form — by design, since there is no test in this
/// suite whose subject-under-test is the login form/flow itself. That investigation also confirmed
/// that HR.Modules.Identity.Services.SupabaseAuthGateway's EnsureDevUserAsync/SignInWithPasswordAsync
/// (used by dev-persona seeding and the real Login feature respectively) CANNOT be faked the way
/// FakeSupabaseAuthGateway already fakes CreateUserAsync/ResendVerificationEmailAsync/etc.:
/// SignInWithPasswordAsync's returned access token is sent as a Bearer token on every subsequent
/// HR.Api call, and HR.Api validates that token as a genuine Supabase-signed JWT (signature checked
/// against Supabase's live JWKS, issuer/audience/lifetime enforced) unconditionally in every
/// environment, including Development/E2E — see HR.Api/Program.cs's ConfigureSupabaseJwtBearer and
/// SupabaseCurrentUserResolutionMiddleware. A fabricated, non-Supabase-signed token would fail that
/// validation on the very next request, so this app deliberately never fakes sign-in.
///
/// That means EVERY login in this suite already exercises the real Supabase password-grant flow —
/// there is no separate "fake" path to guard against drifting from. This test exists purely to make
/// that real dependency an explicit, intentional, and monitored one: a minimal, focused UI login that
/// asserts real Supabase auth still issues a session this app accepts end-to-end. If a future change
/// (accidentally introducing a fake sign-in path, or a genuine Supabase-side regression such as a
/// token format change) breaks real login, this is the test that should fail first and most clearly.
///
/// Deliberately uses LoginPage.RealFormLoginAsync (bypassing PersonaLoginCache) rather than
/// LoginAsync, so this test always drives the actual login form/real Supabase call itself instead of
/// reusing another test's cached session.
/// </summary>
public sealed class RealSupabaseLoginFlowTests : IAsyncLifetime
{
    // Laura Bennett — HR Administrator persona (Employee + HrAdministrator roles, see
    // HR.Modules.Identity.IdentityModule seed data). Manager-only personas (e.g. James Okafor,
    // previously used here) don't have sidebar access to the "People and users > Employees" item
    // exercised by RealFormLogin_ThenCircuitDrivenApiCall_IsAuthenticated_NotTreatedAsAnonymous
    // below, since that nav item requires employee:manage-equivalent HR-administrator permissions
    // a plain Manager doesn't hold. Laura is used elsewhere across this suite (e.g.
    // WorkloadActionsReportTests, HrDashboardTests) as the standard HR Administrator login for
    // exactly this reason.
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
    public async Task RealFormLogin_AuthenticatesAgainstRealSupabase_AndReachesAppShellAsCorrectPersona()
    {
        var login = new LoginPage(_page, _app.WebBaseUrl);

        await login.GoToAsync();

        // Real interactive form submit -> real Supabase password-grant sign-in -> a genuine
        // Supabase-signed JWT that HR.Api's JWT bearer validation must accept for the app shell's
        // own bootstrap API calls to succeed. No faked/mocked auth path is involved anywhere in this
        // call chain (see class remarks above).
        await login.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });

        // Laura is HR Administrator; AppSession.LandingUrl's priority order (see its remarks) puts
        // HR Administrator above Recruiter/Manager/Company Administrator, so Home.razor's
        // post-login redirect lands her on /dashboard/hr, confirming this isn't just "some"
        // authenticated session but specifically Laura Bennett's.
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/hr", _page.Url);

        var userInfo = _page.Locator(".top-bar-user-info");
        await userInfo.WaitForAsync(new() { Timeout = 10_000 });
        var displayedName = await userInfo.InnerTextAsync();
        Assert.Contains("Laura Bennett", displayedName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Regression test for the P1 fix ("circuit-scope token bridging"): Blazor Server creates a
    /// SEPARATE DI scope per interactive circuit, so a bearer token captured only in the negotiating
    /// HTTP request's own (different) DI scope never reached the circuit's own CircuitSessionState —
    /// meaning every interactive-circuit API call after login silently carried NO Authorization
    /// header. That would surface here as the shell either never rendering real data (stuck on a
    /// loading/error state) or the app treating the session as anonymous and bouncing to /login,
    /// even though the login form itself just succeeded.
    ///
    /// This performs a real interactive login (production render mode, prerender disabled per
    /// App.razor) and then triggers a genuine, circuit-driven API call — navigating to the employee
    /// list, which is fetched via HR.Web's EmployeeService/HrApiHttpClientFactory from inside the
    /// already-established circuit, not from the initial pre-render HTTP request. Before the fix,
    /// this exact call would have gone out with no bearer token and HR.Api would have rejected it.
    /// </summary>
    [Fact]
    public async Task RealFormLogin_ThenCircuitDrivenApiCall_IsAuthenticated_NotTreatedAsAnonymous()
    {
        var login = new LoginPage(_page, _app.WebBaseUrl);

        await login.GoToAsync();
        await login.RealFormLoginAsync(Email);

        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        // Navigating to /companies/{id}/employees from an already-open circuit (no full page reload,
        // this is client-side Blazor Server routing) forces the component's OnInitializedAsync to
        // make a fresh HR.Api call through HrApiHttpClientFactory using THIS circuit's own
        // CircuitSessionState — exactly the code path the original bug broke. A page reload here
        // would re-run the initial HTTP request/prerender path instead and could mask the bug, so
        // this deliberately uses in-app navigation via the real sidebar, not GotoAsync.
        //
        // The sidebar is a Syncfusion SfMenu: "Employees" lives inside the "People and users"
        // group flyout (see AdminNavigation.cs), not as a top-level link, and its rendered items
        // expose role="menuitem" rather than role="link" — so GetByRole(Link, "Employees") can
        // never match regardless of auth state. Use the existing SidebarPage helper (the pattern
        // every other sidebar-driven E2E test in this suite already uses) instead of a hand-rolled
        // locator.
        var sidebar = new SidebarPage(_page);
        await sidebar.ClickGroupedMenuItemAsync("People and users", "Employees");

        // If the circuit's bearer token were missing, HR.Api would reject the call as
        // unauthenticated and the app would either show an error/empty state or redirect to
        // /login — asserting we land on and stay on an authenticated employees view, with real
        // row content rendered, rules both of those failure modes out.
        await _page.WaitForURLAsync(new Regex("/employees"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/login", _page.Url);

        var grid = _page.Locator(".e-grid, [data-testid='employee-list']").First;
        await grid.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await grid.IsVisibleAsync());

        // The shell itself must still show Laura Bennett as the authenticated user — proving this
        // wasn't silently downgraded to an anonymous circuit somewhere along the way.
        var userInfo = _page.Locator(".top-bar-user-info");
        await userInfo.WaitForAsync(new() { Timeout = 10_000 });
        var displayedName = await userInfo.InnerTextAsync();
        Assert.Contains("Laura Bennett", displayedName, StringComparison.OrdinalIgnoreCase);
    }
}
