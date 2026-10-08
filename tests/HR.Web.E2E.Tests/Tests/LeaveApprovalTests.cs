using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class LeaveApprovalTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    private const string StartDate = "06/07/2026";
    private const string EndDate   = "10/07/2026";

    [Fact]
    public async Task SubmittingLeave_ThenApprovingAsManager_UpdatesStatusAndBalance()
    {
        var reason = $"E2E-APPROVE-{Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var notif   = new NotificationPanel(_page);
        var task    = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        var initialBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.NotNull(initialBalance);

        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", StartDate, EndDate, reason);
        await profile.SubmitLeaveRequestAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        var pendingStatus = await profile.GetLeaveRequestStatusAsync(reason);
        Assert.Equal("Pending", pendingStatus);

        var pendingBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.Equal(initialBalance, pendingBalance);

        await login.SwitchAccountAsync(JamesEmail);

        await profile.GoToAsync(AcmeId, JamesId);
        await profile.OpenTasksTabAsync();
        var taskTitles = await profile.GetTaskTitlesAsync();
        Assert.Contains(taskTitles, t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        var unread = await notif.GetUnreadCountAsync();
        Assert.True(unread > 0, $"Expected at least 1 unread notification, got {unread}");

        await notif.OpenAsync();
        var notifTitles = await notif.GetNotificationTitlesAsync();
        Assert.Contains(notifTitles, t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)
                                       || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        await notif.ClickNotificationAsync("6 Jul 2026 to 10 Jul 2026");
        await task.WaitForLoadedAsync();

        var taskTitle = await task.GetTitleAsync();
        Assert.Contains("Tom Williams", taskTitle, StringComparison.OrdinalIgnoreCase);

        Assert.True(await task.HasLeaveReviewPanelAsync(),
            "Expected the 'Review Leave Request' panel to be visible on a leave task");

        var source = await task.GetDetailAsync("Source");
        Assert.Equal("Leave", source);

        var status = await task.GetStatusAsync();
        Assert.Equal("Not Started", status);

        await task.ApproveAsync();

        var statusAfter = await task.GetStatusAsync();
        Assert.Equal("Completed", statusAfter);

        Assert.False(await task.HasLeaveReviewPanelAsync(),
            "Review Leave Request panel should be hidden after approval");

        await login.SwitchAccountAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        var approvedStatus = await profile.GetLeaveRequestStatusAsync(reason);
        Assert.Equal("Approved", approvedStatus);

        var finalBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.NotNull(finalBalance);
        Assert.True(finalBalance < initialBalance,
            $"Balance should have decreased after approval: was {initialBalance}, now {finalBalance}");
    }
}
