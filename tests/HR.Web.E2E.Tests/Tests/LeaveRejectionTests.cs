using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

[Collection("E2E")]
public sealed class LeaveRejectionTests : IAsyncLifetime
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    // Use different dates to avoid collision with the approval test if both run on the same DB.
    private const string StartDate = "17/08/2026";
    private const string EndDate   = "21/08/2026"; // Mon–Fri = 5 working days

    private const string RejectionReason = "Insufficient team cover during sprint release";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public LeaveRejectionTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task SubmittingLeave_ThenRejectingWithReason_ShowsRejectedStatusAndReason()
    {
        var reason = $"E2E-REJECT-{Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var dash    = new DashboardPage(_page, _fixture.WebBaseUrl);
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

        // ── Step 5: Dashboard shows the task ─────────────────────────────────
        await dash.GoToAsync();
        var taskTitles = await dash.GetTaskTitlesAsync();
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

        var rejectedStatus = await profile.GetLeaveRequestStatusAsync(reason);
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
