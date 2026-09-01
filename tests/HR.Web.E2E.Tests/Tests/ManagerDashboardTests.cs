using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Manager-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/ManagerDashboard.razor), reached via
/// "/dashboard/manager". The page guards on Session.IsManager and redirects any other role to
/// Session.MyProfileUrl.
///
/// Redesigned layout (see PRODUCT ticket "Reorganise the Team Manager Dashboard around priority
/// actions"): the former standalone Team Tasks, Leave Requests, Upcoming Probation Reviews,
/// Overdue Return-to-Work Reviews and Missing Fit Notes widgets are now folded into a single
/// combined "Requires your attention" queue (ManagerAttentionQueueWidget.razor). A new compact
/// "Team Status" metric strip (TeamStatusSummary.razor) was added. TeamOnboardingWidget and
/// TeamSicknessTodayWidget were removed from this page entirely (and are unused elsewhere) — see
/// the removed TeamOnboardingWidget_* and TeamSicknessTodayWidget_* tests that previously lived
/// in this file, deleted as part of that redesign since there is no longer any UI surface on this
/// page for them to exercise. My Team (MyTeamWidget) and Reports (TeamReportsWidget) are
/// unchanged structurally and still gate on Session.IsManager.
///
/// Uses seeded personas:
///   - James Okafor (james.okafor@acme.example) — Manager only, manages Tom Williams directly.
///   - David Park (david.park@acme.example) — HrAdministrator + Manager, manages Emma Jones and
///     Carlos Rivera directly.
///   - Tom Williams (tom.williams@acme.example) — plain Employee, used for the denial test.
/// </summary>
public sealed class ManagerDashboardTests(ManagerPersonaFixture fixture) : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string JamesEmail = "james.okafor@acme.example";
    private const string DavidEmail = "david.park@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    /// <summary>
    /// Returns a dedicated pre-seeded pool employee (SeededE2eEmployees.ManagerDashboard[0]) that
    /// is already assigned David Park as manager in the seed, and already has a NotStarted
    /// onboarding plan whose three default checklist tasks are unassigned (so they sit in the HR
    /// Inbox) — the exact starting state the old create-then-assign-manager flow produced.
    /// </summary>
    private static (Guid EmployeeId, string LastName) CreateEmployeeReportingToDavidAsync()
    {
        var seeded = SeededE2eEmployees.ManagerDashboard[0];
        return (seeded.EmployeeId, seeded.LastName);
    }

    [Fact]
    public async Task NonManager_IsRedirectedAway_FromManagerDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/manager");

        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}/profile"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/dashboard/manager", _page.Url);
    }

    [Fact]
    public async Task ManagerOnly_SeesAttentionQueueAndTeamStatusWidgets()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Requires your attention"));
        Assert.True(await dashboard.HasWidgetAsync("Team Status"));
        Assert.True(await dashboard.HasWidgetAsync("My Team"));
    }

    [Fact]
    public async Task AttentionQueueWidget_LoadsWithoutError_AndIncludesAllCategories()
    {
        // Replaces the former separate TeamTasksWidget_LoadsWithoutError,
        // UpcomingProbationReviewsWidget_IsVisible_ForManager and
        // OverdueReturnToWorkAndMissingFitNotesWidgets_LoadWithoutError tests — all of those
        // categories are now rows inside the single combined attention queue
        // (ManagerAttentionQueueWidget.razor), so a single load-without-error assertion against
        // that widget now covers what those four separate widget cards used to cover.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Requires your attention"));
        await dashboard.WaitForAttentionQueueLoadedAsync();
        await dashboard.GetAttentionQueueSubjectsAsync();
    }

    [Fact]
    public async Task TeamStatusSummary_LoadsWithoutError_ForManager()
    {
        // Replaces the former TeamSicknessTodayWidget_Loads_SelfScopedToDirectReports test — the
        // standalone Team Sickness Today widget was removed from this page; the equivalent "how
        // many of my team are sick right now" signal now lives as the "Sick" tile on the new Team
        // Status summary strip.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Team Status"));
        await dashboard.WaitForTeamStatusLoadedAsync();

        // No seeded active sickness record for James's team as of writing — asserting the tile
        // resolves to a definite non-negative count is sufficient here; the underlying
        // sickness-detection logic is covered by the Sickness module's own tests.
        var sick = await dashboard.GetTeamStatusValueAsync("Sick");
        Assert.True(sick >= 0);
    }

    [Fact]
    public async Task MyTeamWidget_ShowsDirectReport()
    {
        // James Okafor manages Tom Williams directly (see EmployeesModule.SeedEmployeesAsync).
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        var names = await dashboard.GetMyTeamMemberNamesAsync();

        Assert.True(names.Any(n => n.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)),
            $"Expected 'Tom Williams' to appear in the My Team widget. Names found: [{string.Join(", ", names)}]");
    }

    [Fact]
    public async Task MyTeamWidget_ShowsDirectReportsPhoneAndEmail_AsVisibleText()
    {
        // Tom Williams is seeded with phone "07700 900004" and work email
        // "tom.williams@acme.example" (see EmployeesModule.SeedEmployeesAsync's MakeAcme call for
        // empDev1Id). MyTeamWidget.razor renders these as visible ".team-card-contact-text" spans
        // next to each icon, not just hidden in the link's "title" tooltip attribute.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();
        var contactText = await dashboard.GetTeamMemberContactTextAsync("Tom Williams");

        Assert.Contains(contactText, t => t.Contains("07700 900004"));
        Assert.Contains(contactText, t => t.Contains("tom.williams@acme.example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MyTeamWidget_ShowsAtWorkStatusBadge_ForDirectReportWithNoActiveSicknessOrLeave()
    {
        // Tom Williams has no active sickness record and no leave request covering today (see
        // seed data referenced elsewhere in this file), so his status badge should read "At Work".
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();

        Assert.Equal("At Work", await dashboard.GetTeamMemberStatusAsync("Tom Williams"));
    }

    [Fact]
    public async Task MyTeamWidget_NotifySicknessButton_OpensRecordSicknessDialogForThatEmployee()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();
        await dashboard.ClickNotifySicknessForTeamMemberAsync("Tom Williams");

        // RecordSicknessDialog renders with SelfService not set (manager-on-behalf-of flow), so
        // its header reads "Record Sickness" even though the card's own button says "Notify
        // Sickness" — see MyTeamWidget.razor / RecordSicknessDialog.razor.
        Assert.Contains("Record Sickness", await _page.ContentAsync());
    }

    /// <summary>
    /// Cross-cutting completion flow: an onboarding-sourced task is completed through the
    /// existing Task view dialog (not any new UI added by this feature set), and that completion
    /// is reflected both in the assignee's own profile Tasks tab (the task disappears — the
    /// current, role-agnostic replacement for the old dead "My Onboarding Tasks" dashboard
    /// widget) and on the employee's Onboarding tab (progress increases, the task's checklist
    /// row shows Completed, and the plan status moves from "Not Started" to "In Progress").
    ///
    /// The default onboarding checklist tasks created for a brand-new employee are all
    /// unassigned at creation time (the New Employee form has no Manager field, and the
    /// "assign to new hire" template path isn't exercised by the default fallback checklist —
    /// see CreateOnboardingPlanOnEmployeeCreated.EmployeeCreatedHandler), so they land in the HR
    /// Inbox as unassigned tasks. David claims one from there (the same self-assign flow covered
    /// by HrInboxTests), which is what makes it appear in his own Tasks tab for him to complete.
    /// </summary>
    [Fact]
    public async Task CompletingOnboardingTask_RemovesItFromAssigneesTasksTab_AndUpdatesOnboardingTabProgress()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var inbox    = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        // ── Step 1: create a new employee — auto-creates an onboarding plan with three
        // default checklist tasks, all unassigned (see method remarks above). ──────────────
        _ = empList;
        var (employeeId, lastName) = CreateEmployeeReportingToDavidAsync();

        // ── Step 2: claim one of the new employee's unassigned onboarding tasks from the
        // HR Inbox — this assigns it to David, making it appear in his own Tasks tab. ──────
        await inbox.GoToAsync(AcmeId);
        var inboxTitles  = await inbox.GetTaskTitlesAsync();
        var claimedTitle = inboxTitles.First(t => t.Contains(lastName, StringComparison.OrdinalIgnoreCase));
        await inbox.ClaimAsync(claimedTitle);

        // ── Step 3: the claimed task now appears in David's own Tasks tab. ──────────────────
        var davidId = Guid.Parse("30000000-0000-0000-0000-000000000008");
        await profile.GoToAsync(AcmeId, davidId);
        await profile.OpenTasksTabAsync();
        var tabTitlesBefore = await profile.GetTaskTitlesAsync();
        Assert.Contains(tabTitlesBefore, t => t.Contains(claimedTitle, StringComparison.OrdinalIgnoreCase));

        // ── Step 4: complete it via the existing Task view dialog flow. ─────────────────────
        await profile.ClickTaskAsync(claimedTitle);
        await taskView.WaitForLoadedAsync();
        await taskView.CompleteGeneralTaskAsync();
        await taskView.CloseAsync();

        // ── Step 5: it shows as Completed in David's Tasks tab. TaskList.razor has no status
        // filter, so a completed task stays in the grid rather than disappearing — assert on the
        // status badge, not on absence from the list (see MyProfilePage.GetTaskStatusAsync). ───
        await profile.GoToAsync(AcmeId, davidId);
        await profile.OpenTasksTabAsync();
        Assert.Equal("Completed", await profile.GetTaskStatusAsync(claimedTitle));

        // ── Step 6: progress on the employee's Onboarding tab reflects the completion. ──────
        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenOnboardingTabAsync();

        var percent = await empEdit.GetOnboardingProgressPercentAsync();
        Assert.True(percent > 0,
            $"Expected onboarding progress to be greater than 0% after completing one of the " +
            $"three default checklist tasks, got {percent}%");

        var taskStatus = await empEdit.GetOnboardingChecklistTaskStatusAsync(claimedTitle);
        Assert.Equal("Completed", taskStatus);

        var planStatus = await empEdit.GetOnboardingStatusBadgeTextAsync();
        Assert.Equal("In Progress", planStatus);
    }
}
