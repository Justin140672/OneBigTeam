using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

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

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        await _page.GotoAsync(
            $"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{TomId}");

        await WaitForUrlToStopContainingAsync($"/employees/{TomId}");

        var finalUrl = _page.Url;
        Assert.DoesNotContain($"/employees/{TomId}", finalUrl);
    }

    [Fact]
    public async Task Employee_CannotAccess_HrInbox()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new HrInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        var target = $"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr/inbox";
        await _page.GotoAsync(target);

        try
        {
            await _page.WaitForURLAsync(u => !u.Contains("/hr/inbox"), new() { Timeout = 25_000 });
        }
        catch (TimeoutException) { /* fall through — may still be a valid in-page deny */ }

        // ── Step 3: The app must NOT show HR inbox content ────────────────────
        // It should redirect to login, show an access-denied alert, or render a different page.
        var url = _page.Url;
        var isOnHrInbox = url.Contains("/hr/inbox");

        if (isOnHrInbox)
        {
            var content = await _page.ContentAsync();
            var hasInboxContent = content.Contains("inbox-card") ||
                                  await _page.Locator(".inbox-card").IsVisibleAsync();

            Assert.False(hasInboxContent,
                "Tom (Employee) should not see HR Inbox task cards");

            Assert.True(
                await _page.Locator(".alert-danger, [class*='unauthorized'], [class*='forbidden']").IsVisibleAsync()
                || content.Contains("not authorised", StringComparison.OrdinalIgnoreCase)
                || content.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Access denied", StringComparison.OrdinalIgnoreCase),
                "Expected an access-denied message when an Employee navigates to the HR Inbox");
        }
        else
        {
            Assert.DoesNotContain("/hr/inbox", url);
        }
    }

    [Fact]
    public async Task Employee_CannotAccess_AnotherEmployeesAdminProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync(
            $"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{JamesId}");
        await WaitForUrlToStopContainingAsync($"/employees/{JamesId}");

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
        await WaitForUrlAsync(u =>
            !u.TrimEnd('/').EndsWith($"/companies/{AcmeId}/employees", StringComparison.OrdinalIgnoreCase));

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/employees", StringComparison.OrdinalIgnoreCase),
            $"Expected Tom to be redirected away from the employee list page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task Employee_DoesNotSee_AdminSidebarNav()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        var navMenu = _page.Locator(".app-nav-menu");
        await Assertions.Expect(navMenu).ToBeHiddenAsync(new() { Timeout = 10_000 });
    }


}
