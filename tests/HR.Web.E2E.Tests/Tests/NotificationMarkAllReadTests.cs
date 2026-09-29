using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class NotificationMarkAllReadTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    private const string StartDate = "02/11/2026";
    private const string EndDate   = "06/11/2026";

    [Fact]
    public async Task Submitting_Leave_Then_Marking_All_Notifications_Read_Clears_Unread_Badge()
    {
        var reason = $"E2E-MARK-ALL-READ-{Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var notif   = new NotificationPanel(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", StartDate, EndDate, reason);
        await profile.SubmitLeaveRequestAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        await login.SwitchAccountAsync(JamesEmail);

        var unreadBefore = await notif.GetUnreadCountAsync();
        Assert.True(unreadBefore > 0,
            $"Expected James to have at least 1 unread notification before marking read, got {unreadBefore}");

        await notif.OpenAsync();
        await notif.MarkAllReadAsync();

        var unreadAfter = await notif.GetUnreadCountAsync();
        Assert.Equal(0, unreadAfter);
    }
}
