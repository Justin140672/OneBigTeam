using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies LeaveRequestsWidget.razor's NavigateToRequest behavior on both the HR and Manager
/// dashboards: clicking a leave request row opens TaskViewDialog in place when the request still
/// has an open (not yet approved/rejected) leave-approval task — the same dialog already used by
/// the notification bell (see LeaveApprovalTests, NotificationMarkAllReadTests) — and otherwise
/// falls back to navigating to the employee's admin profile with the Leave tab active
/// (EmployeeEdit.razor's "?tab=leave", not the self-service "/profile" route — MyProfile.razor
/// redirects away from any EmployeeId that isn't the signed-in user's own).
///
/// Submitting leave via Tom's own profile (MyProfilePage) fires LeaveRequestedIntegrationEvent,
/// which HR.Modules.Tasks.Features.LeaveRequested.LeaveRequestedHandler turns into an open
/// leave-approval task assigned to the requester's manager (James Okafor for Tom Williams — see
/// EmployeesModule.SeedEmployeesAsync). GetRecentLeaveRequestsHandler only exposes a non-null
/// TaskId for a leave request while that task is still open, and for a non-HR-administrator
/// viewer (e.g. James, a plain Manager) only Pending requests are returned at all — so the
/// "already actioned" fallback scenario below uses an HR Administrator (Laura Bennett), whose
/// company-wide, all-statuses view keeps showing the request (with a null TaskId) after James
/// approves it.
/// </summary>
public sealed class LeaveRequestsWidgetTaskDialogTests(CrossUserFixture fixture) : CrossUserLeaveNotificationsTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ClickingPendingLeaveRequest_OnManagerDashboard_OpensTaskDialog_InsteadOfNavigating()
    {
        var reason = $"E2E-WIDGET-DIALOG-{Guid.NewGuid():N}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Tom submits a pending leave request — creates an open review task
        // assigned to James, his manager. ───────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", "01/09/2026", "03/09/2026", reason);
        await profile.SubmitLeaveRequestAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        // ── Step 2: Switch to James (Tom's manager) and go to the Manager Dashboard. ────
        await login.SwitchAccountAsync(JamesEmail);
        await dashboard.GoToAsync();

        // ── Step 3: Click Tom's row in the combined "Requires your attention" queue —
        // LeaveRequestsWidget's standalone card was folded into ManagerAttentionQueueWidget by
        // the Manager Dashboard redesign, so leave-request rows now live there, filterable by
        // the "Leave request" category text rendered in each row's ".task-widget-meta". ───────
        var namesBeforeClick = await dashboard.GetAttentionQueueSubjectsAsync("Leave request");
        Assert.Contains(namesBeforeClick, n => n.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase));

        await dashboard.ClickAttentionQueueItemAsync("Tom Williams");

        // ── Step 4: The Task view dialog opens in place — no navigation away from the
        // dashboard, unlike the "already actioned" fallback. ────────────────────────────
        await task.WaitForLoadedAsync();
        Assert.Contains("/dashboard/manager", _page.Url);

        Assert.True(await task.HasLeaveReviewPanelAsync(),
            "Expected the 'Review Leave Request' panel to be visible for Tom's still-open leave task");

        var title = await task.GetTitleAsync();
        Assert.Contains("Tom Williams", title, StringComparison.OrdinalIgnoreCase);

        var status = await task.GetStatusAsync();
        Assert.Equal("Not Started", status);
    }

    [Fact]
    public async Task ClickingAlreadyActionedLeaveRequest_OnHrDashboard_IsANoOp()
    {
        var reason = $"E2E-WIDGET-FALLBACK-{Guid.NewGuid():N}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var hrDash    = new HrDashboardPage(_page, _fixture.WebBaseUrl);
        var task      = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Tom submits a pending leave request. ────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", "14/09/2026", "16/09/2026", reason);
        await profile.SubmitLeaveRequestAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        // ── Step 2: James (Tom's manager) approves it via his own Tasks tab — closing the
        // review task, matching the "Completing" flow used elsewhere in this suite (see
        // LeaveApprovalTests, ManagerDashboardTests.CompletingOnboardingTask...). ─────────
        await login.SwitchAccountAsync(JamesEmail);

        var jamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        await profile.GoToAsync(AcmeId, jamesId);
        await profile.OpenTasksTabAsync();
        var taskTitles = await profile.GetTaskTitlesAsync();
        var reviewTitle = taskTitles.First(t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase));

        await profile.ClickTaskAsync(reviewTitle);
        await task.WaitForLoadedAsync();
        await task.ApproveAsync();
        await task.CloseAsync();

        // ── Step 3: Switch to Laura (HR Administrator) — the HR dashboard's leave requests are
        // now folded into AttentionQueueWidget ("Needs your attention"), which hides resolved
        // (Approved/Declined/Rejected) leave requests by default (see AttentionQueueWidget.razor's
        // _resolvedLeaveItems / "Show resolved leave requests" toggle) — a behavior change from
        // the old standalone LeaveRequestsWidget, which always listed every status company-wide
        // for an HR Administrator. ────────────────────────────────────────────────────────────
        await login.SwitchAccountAsync(LauraEmail);
        await hrDash.GoToAsync();

        var subjectsBeforeToggle = await hrDash.GetAttentionQueueSubjectsAsync();
        var tomRowsBeforeToggle = subjectsBeforeToggle.Count(n => n.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase));

        // ── Step 4: Enable the toggle — the now-Approved request should appear. Only a Pending
        // request is actionable (AttentionQueueWidget's IsPending check); Tom's now-Approved
        // request renders with OnActivate as a no-op, so clicking it neither opens a dialog nor
        // navigates. ──────────────────────────────────────────────────────────────────────────
        await hrDash.SetShowResolvedLeaveRequestsAsync(true);
        var subjectsAfterToggle = await hrDash.GetAttentionQueueSubjectsAsync();
        var tomRowsAfterToggle = subjectsAfterToggle.Count(n => n.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase));
        Assert.True(tomRowsAfterToggle > tomRowsBeforeToggle,
            "Expected enabling 'Show resolved leave requests' to reveal Tom's now-Approved request.");

        // Disambiguate by date ("14 Sep", from this test's own request) rather than name alone —
        // the sibling ClickingPendingLeaveRequest_... test deliberately leaves its own Tom
        // Williams request (with a different date) with an open task, so a name-only match could
        // land on that row instead. ClickAttentionQueueItemAsync filters on the row's full text
        // content, which includes the rendered date range, so "14 Sep" alone is enough to target
        // this specific row uniquely.
        var urlBeforeClick = _page.Url;
        await hrDash.ClickAttentionQueueItemAsync("14 Sep");

        // No dialog and no navigation — give any (incorrect) async effect a moment to surface
        // before asserting its absence.
        await _page.WaitForTimeoutAsync(500);
        Assert.Equal(urlBeforeClick, _page.Url);
        Assert.False(await task.IsVisibleAsync(),
            "Did not expect the task dialog to open for an already-actioned (non-Pending) leave request");
    }
}
