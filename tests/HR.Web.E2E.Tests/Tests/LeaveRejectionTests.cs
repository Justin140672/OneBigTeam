using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class LeaveRejectionTests(CrossUserFixture fixture) : CrossUserLeaveNotificationsTestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    // Use different dates to avoid collision with the approval test if both run on the same DB.
    private const string StartDate = "17/08/2026";
    private const string EndDate   = "21/08/2026"; // Mon–Fri = 5 working days

    private const string RejectionReason = "Insufficient team cover during sprint release";

    [Fact]
    public async Task SubmittingLeave_ThenRejectingWithReason_ShowsRejectedStatusAndReason()
    {
        var reason = $"E2E-REJECT-{Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var notif   = new NotificationPanel(_page);
        var task    = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom and record balance ───────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        var initialBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.NotNull(initialBalance);

        // ── Step 2: Submit a 5-day annual leave request ───────────────────────
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", StartDate, EndDate, reason);
        await profile.SubmitLeaveRequestAsync();

        // ── Step 3: Verify pending ────────────────────────────────────────────
        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        // ── Step 4: Switch to James ───────────────────────────────────────────
        await login.SwitchAccountAsync(JamesEmail);

        // ── Step 5: James's own Tasks tab shows the task ──────────────────────
        // The old dashboard "My Tasks" widget (MyTasksWidget.razor) this used to check is dead
        // code — no longer rendered anywhere. James's own profile Tasks tab is the current,
        // role-agnostic place to find his full assigned-task list.
        await profile.GoToAsync(AcmeId, JamesId);
        await profile.OpenTasksTabAsync();
        var taskTitles = await profile.GetTaskTitlesAsync();
        Assert.Contains(taskTitles, t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        // ── Step 6: Notification bell has unread notification ─────────────────
        var unread = await notif.GetUnreadCountAsync();
        Assert.True(unread > 0, $"Expected at least 1 unread notification, got {unread}");

        await notif.OpenAsync();
        var notifTitles = await notif.GetNotificationTitlesAsync();
        Assert.Contains(notifTitles, t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)
                                       || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        // ── Step 7: Navigate to task via notification ─────────────────────────
        await notif.ClickNotificationAsync("Tom Williams");
        await task.WaitForLoadedAsync();

        // ── Step 8: Verify task details ───────────────────────────────────────
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

        // After rejection the Reason column shows RejectionReason, not the original leave reason.
        var rejectedStatus = await profile.GetLeaveRequestStatusAsync(reason, RejectionReason);
        Assert.Equal("Rejected", rejectedStatus);

        // The rejection reason is stored in the leave request's Reason field and displayed in the table.
        // Verify it is visible somewhere on the leave tab.
        var pageContent = await _page.ContentAsync();
        Assert.Contains(RejectionReason, pageContent, StringComparison.OrdinalIgnoreCase);

        // Balance should be unchanged — rejected leave does not consume allowance.
        var finalBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.Equal(initialBalance, finalBalance);
    }
}
