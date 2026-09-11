using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// WRITTEN BUT NOT RUN — do not run without a live browser/full environment; verify manually before
/// merging. This environment cannot run Playwright/E2E tests or start a dev server (project policy).
/// This file has never been executed.
///
/// Ticket 13 — cross-tab logout enforcement. Two independent <see cref="Microsoft.Playwright.IBrowserContext"/>s
/// (two genuinely separate browser sessions/cookie jars, from the same <see cref="Microsoft.Playwright.IBrowser"/>)
/// both log in as the same persona (Laura Bennett — HR Administrator, same persona used by
/// <see cref="RealSupabaseLoginFlowTests"/>). Context A then logs out; context B, which never touched
/// logout, must be rejected on its very next authenticated navigation — proving the server-side
/// revocation record (HR.Modules.Identity.Domain.SessionRevocation, keyed by SupabaseAuthUserId, not
/// by any client-held state) is what enforces this, not something local to context A's own tab/cookie.
///
/// Design note on Supabase's own (upstream, best-effort) sign-out call: this test does NOT depend on
/// Supabase's GoTrue logout call succeeding or being fast. HR.Modules.Identity.Features.Logout.Handler
/// writes the local identity.session_revocations record synchronously, BEFORE attempting the upstream
/// Supabase call, and HR.Api's OnTokenValidated pipeline (SupabaseJwtBearerConfiguration) checks that
/// local record on every subsequent request — so context B's rejection here is caused purely by that
/// local write completing before /logout responds to context A, regardless of what Supabase itself
/// does afterward. The specific claim "revocation is still recorded even when the upstream Supabase
/// call fails" is already authoritatively proven at the unit level by
/// LogoutHandlerTests.Records_Revocation_Even_When_Upstream_Supabase_Sign_Out_Fails (see
/// tests/HR.Modules.Identity.Tests/LogoutHandlerTests.cs) — faking a Supabase outage from inside a
/// live-browser Playwright test is impractical and would not add anything beyond what that unit test
/// already covers, so it is deliberately not re-derived here.
/// </summary>
public sealed class CrossTabLogoutEnforcementTests : IAsyncLifetime
{
    // Laura Bennett — HR Administrator persona, same as RealSupabaseLoginFlowTests /
    // CircuitInvalidationBlocksReauthenticationTests. Reused so this test can lean on the same
    // known-working "authenticated as Laura" sidebar/top-bar assertions those tests already establish.
    private const string Email = "laura.bennett@acme.example";

    private AppFixture _app = null!;
    private Microsoft.Playwright.IBrowserContext _contextA = null!;
    private Microsoft.Playwright.IBrowserContext _contextB = null!;
    private Microsoft.Playwright.IPage _pageA = null!;
    private Microsoft.Playwright.IPage _pageB = null!;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();

        // Two entirely separate browser contexts = two separate cookie jars/sessions, simulating two
        // different browser tabs/windows (or two different devices) logged in as the same user.
        _contextA = await _app.Browser.NewContextAsync();
        _contextB = await _app.Browser.NewContextAsync();

        _pageA = await _contextA.NewPageAsync();
        _pageB = await _contextB.NewPageAsync();

        foreach (var page in new[] { _pageA, _pageB })
        {
            page.SetDefaultTimeout(30_000);
            page.SetDefaultNavigationTimeout(30_000);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var page in new[] { _pageA, _pageB })
        {
            try { await page.GotoAsync("about:blank"); } catch { /* ignore navigation errors on teardown */ }
        }

        await _contextA.DisposeAsync();
        await _contextB.DisposeAsync();
        await SharedAppFixture.ReleaseAsync();
    }

    [Fact]
    public async Task LogoutInOneContext_RejectsTheOtherContexts_SameUser_LiveSession()
    {
        var loginA = new LoginPage(_pageA, _app.WebBaseUrl);
        var loginB = new LoginPage(_pageB, _app.WebBaseUrl);

        // Independent, real interactive logins in both contexts, same persona. Each drives the real
        // Supabase password-grant sign-in (see RealSupabaseLoginFlowTests's remarks on why this suite
        // never fakes sign-in), so each context ends up with its own genuine Supabase-signed session.
        await loginA.GoToAsync();
        await loginA.RealFormLoginAsync(Email);
        await _pageA.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _pageA.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        await loginB.GoToAsync();
        await loginB.RealFormLoginAsync(Email);
        await _pageB.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });
        await _pageB.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        // Sanity: context B is genuinely live and authenticated before logout happens anywhere,
        // via a real circuit-driven API call (same technique RealSupabaseLoginFlowTests uses).
        var sidebarB = new SidebarPage(_pageB);
        await sidebarB.ClickGroupedMenuItemAsync("People and users", "Employees");
        await _pageB.WaitForURLAsync(new Regex("/employees"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/login", _pageB.Url);
        var gridBeforeLogout = _pageB.Locator(".e-grid, [data-testid='employee-list']").First;
        await gridBeforeLogout.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await gridBeforeLogout.IsVisibleAsync());

        // Context A logs out. HR.Web's "/logout" minimal API endpoint (Program.cs) must be reached via
        // a genuine hard browser navigation — matches the pattern EmployeeCompletionDialog.razor uses
        // (hardNavigate("/logout")) rather than Blazor's own NavigateTo, so this exercises the real
        // production sign-out request path end-to-end (cookie access token forwarded as the bearer to
        // HR.Api's POST /api/logout, which writes the session_revocations row before attempting the
        // upstream Supabase call).
        await _pageA.GotoAsync($"{_app.WebBaseUrl}/logout");
        await _pageA.WaitForURLAsync(new Regex("/login"), new() { Timeout = 15_000 });

        // Context B never touched logout. Its live circuit's next authenticated action must now be
        // rejected — HR.Api's JWT bearer pipeline rejects the still-technically-unexpired bearer token
        // once its "iat" is at or before the revocation instant recorded by context A's logout (see
        // SupabaseJwtBearerConfiguration.OnTokenValidated / IdentityModule.IsSessionRevokedAsync), and
        // AppSessionAuthStateProvider/CircuitSessionState surface that as a forced navigation back to
        // /login (mirrors the "poisoned circuit" navigation already exercised by
        // CircuitInvalidationBlocksReauthenticationTests). Force a fresh circuit-driven navigation
        // (not GotoAsync, so this exercises the live circuit's own reconnect/re-auth path rather than
        // a fresh page load, which would just naturally 401 on its own and prove nothing about
        // cross-tab enforcement of an already-open session).
        var sidebarAfterLogout = new SidebarPage(_pageB);
        try
        {
            await sidebarAfterLogout.ClickGroupedMenuItemAsync("People and users", "Employees");
        }
        catch
        {
            // A thrown navigation/locator failure here is itself acceptable evidence the circuit was
            // already forced off — fall through to the authoritative URL assertion below.
        }

        await _pageB.WaitForURLAsync(new Regex("/login"), new() { Timeout = 20_000 });
        Assert.Contains("/login", _pageB.Url);
    }
}
