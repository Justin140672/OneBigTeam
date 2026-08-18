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
    // James Okafor — Manager-only persona, chosen because he's not the sole login target of a
    // role-fixed fixture's own bootstrap in a way that would race this test's own uncached, real
    // interactive login for the same persona (see PersonaLoginCache's per-persona real-login gate).
    private const string Email = "james.okafor@acme.example";

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

        // James is Manager-only; Home.razor's post-login redirect lands him on /dashboard/manager
        // (see SidebarNavigationTests for the same persona/redirect pairing), confirming this isn't
        // just "some" authenticated session but specifically James Okafor's.
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/manager", _page.Url);

        var userInfo = _page.Locator(".top-bar-user-info");
        await userInfo.WaitForAsync(new() { Timeout = 10_000 });
        var displayedName = await userInfo.InnerTextAsync();
        Assert.Contains("James Okafor", displayedName, StringComparison.OrdinalIgnoreCase);
    }
}
