using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class LeaveRejectionTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    // Use different dates to avoid collision with the approval test if both run on the same DB.
    private const string StartDate = "17/08/2026";
    private const string EndDate   = "21/08/2026";

    private const string RejectionReason = "Insufficient team cover during sprint release";

    [Fact]
    public async Task SubmittingLeave_ThenRejectingWithReason_ShowsRejectedStatusAndReason()
    {
        var reason = $"E2E-REJECT-{Guid.NewGuid():N}";

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
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        await login.SwitchAccountAsync(JamesEmail);

        await profile.GoToAsync(AcmeId, JamesId);
        await profile.OpenTasksTabAsync();

        IReadOnlyList<string> taskTitles = [];
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            taskTitles = await profile.GetTaskTitlesAsync();
            if (taskTitles.Count > 0) break;
            await _page.WaitForTimeoutAsync(1_000);
            await profile.GoToAsync(AcmeId, JamesId);
            await profile.OpenTasksTabAsync();
        }
        Assert.Contains(taskTitles, t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        var unread = await notif.GetUnreadCountAsync();
        Assert.True(unread > 0, $"Expected at least 1 unread notification, got {unread}");

        await notif.OpenAsync();
        var notifTitles = await notif.GetNotificationTitlesAsync();
        Assert.Contains(notifTitles, t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)
                                       || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        await notif.ClickNotificationAsync("17 Aug 2026 to 21 Aug 2026");
        await task.WaitForLoadedAsync();

        var taskTitle = await task.GetTitleAsync();
        Assert.Contains("Tom Williams", taskTitle, StringComparison.OrdinalIgnoreCase);

        Assert.True(await task.HasLeaveReviewPanelAsync());
        Assert.Equal("Not Started", await task.GetStatusAsync());
        Assert.Equal("Leave", await task.GetDetailAsync("Source"));

        // ── Step 9: Enter a rejection reason and reject ───────────────────────
        await task.EnterDecisionReasonAsync(RejectionReason);
        await task.RejectAsync();

        Assert.Equal("Completed", await task.GetStatusAsync());

        // ── Step 10: Switch back to Tom and verify rejected status + reason ───
        await login.SwitchAccountAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });

        var rejectedStatus = await profile.GetLeaveRequestStatusAsync(reason, RejectionReason);
        Assert.Equal("Rejected", rejectedStatus);

        var pageContent = await _page.ContentAsync();
        Assert.Contains(RejectionReason, pageContent, StringComparison.OrdinalIgnoreCase);
    }
}
