using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that access controls are enforced across the application:
/// - A plain Employee cannot access the HR Inbox.
/// - A plain Employee cannot access another employee's admin profile.
/// - An unauthenticated request to a protected page is redirected to login.
/// </summary>
public sealed class UnauthorizedAccessTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string TomEmail = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    [Fact]
    public async Task Manager_CannotAccess_AnotherEmployeesAdminProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as James (Manager-only — no HrAdministrator role) ───
        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        // ── Step 2: Attempt to navigate to Tom's admin edit page ──────────────
        // James manages Tom directly, so this is a realistic "manager trying to view a direct
        // report's full HR record" scenario, not an arbitrary stranger's profile.
        await _page.GotoAsync(
            $"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{TomId}");

        // ── Step 3: Must be redirected away from the admin edit URL ───────────
        // See E2ETestBase.WaitForUrlToStopContainingAsync's doc comment: the redirect is a
        // client-side Blazor navigation, not a full page load, so WaitForLoadStateAsync(NetworkIdle)
        // doesn't reliably observe it — read _page.Url immediately after can race the redirect.
        await WaitForUrlToStopContainingAsync($"/employees/{TomId}");

        var finalUrl = _page.Url;
        Assert.DoesNotContain($"/employees/{TomId}", finalUrl);
    }

    [Fact]
    public async Task Employee_CannotAccess_HrInbox()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new HrInboxPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom (plain Employee role) ────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Attempt to navigate to the HR Inbox ───────────────────────
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr/inbox");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // ── Step 3: The app must NOT show HR inbox content ────────────────────
        // It should redirect to login, show an access-denied alert, or render a different page.
        var url = _page.Url;
        var isOnHrInbox = url.Contains("/hr/inbox");

        if (isOnHrInbox)
        {
            // If still on the inbox URL, the page must show an access-denied message
            // and NOT render any inbox task cards.
            var content = await _page.ContentAsync();
            var hasInboxContent = content.Contains("inbox-card") ||
                                  await _page.Locator(".inbox-card").IsVisibleAsync();

            Assert.False(hasInboxContent,
                "Tom (Employee) should not see HR Inbox task cards");

            // There should be an access-denied alert or similar.
            Assert.True(
                await _page.Locator(".alert-danger, [class*='unauthorized'], [class*='forbidden']").IsVisibleAsync()
                || content.Contains("not authorised", StringComparison.OrdinalIgnoreCase)
                || content.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Access denied", StringComparison.OrdinalIgnoreCase),
                "Expected an access-denied message when an Employee navigates to the HR Inbox");
        }
        else
        {
            // Redirected away from the inbox — that counts as access being denied.
            Assert.DoesNotContain("/hr/inbox", url);
        }
    }

    [Fact]
    public async Task Employee_CannotAccess_AnotherEmployeesAdminProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom (plain Employee) ─────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Attempt to navigate to James's admin edit page ─────────────
        // This is the admin route (/employees/{id}) — not the self-service profile route.
        await _page.GotoAsync(
            $"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{JamesId}");
        // See E2ETestBase.WaitForUrlToStopContainingAsync's doc comment: the redirect is a
        // client-side Blazor NavigateTo, not a full navigation, so NetworkIdle is not a reliable
        // completion signal.
        await WaitForUrlToStopContainingAsync($"/employees/{JamesId}");

        // ── Step 3: Must be redirected away from the admin edit URL ───────────
        var finalUrl = _page.Url;
        Assert.DoesNotContain($"/employees/{JamesId}", finalUrl);
    }

    [Fact]
    public async Task Employee_CannotAccess_EmployeeList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // Tom is now redirected to his own profile (which itself lives under an "/employees/"
        // path segment), not the dashboard — so check he's off the bare list route specifically,
        // rather than asserting "/employees" doesn't appear anywhere in the URL.
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/employees", StringComparison.OrdinalIgnoreCase),
            $"Expected Tom to be redirected away from the employee list page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task Employee_DoesNotSee_AdminSidebarNav()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom (plain Employee) ─────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Sidebar nav items (People, Leave, HR) must be hidden ──────
        var navMenu = _page.Locator(".app-nav-menu");
        Assert.False(await navMenu.IsVisibleAsync(),
            "Tom (Employee) should not see the admin navigation menu in the sidebar");
    }


}
