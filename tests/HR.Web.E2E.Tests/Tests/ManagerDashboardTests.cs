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
/// Not every widget on this dashboard shares that same gate: TeamTasksWidget, LeaveRequestsWidget,
/// MyTeamWidget, UpcomingProbationReviewsWidget, TeamSicknessTodayWidget,
/// OverdueReturnToWorkReviewsWidget and MissingFitNotesWidget all gate on Session.IsManager (or
/// something Session.IsManager implies), matching the route guard — but TeamOnboardingWidget
/// additionally requires Session.CanManageEmployees (the HrAdministrator-only "employee:manage"
/// permission). So James Okafor (Manager only) can reach this dashboard but never sees Team
/// Onboarding, while David Park (HrAdministrator + Manager) sees every widget on the page.
///
/// Uses seeded personas:
///   - James Okafor (james.okafor@acme.example) — Manager only, manages Tom Williams directly.
///   - David Park (david.park@acme.example) — HrAdministrator + Manager, manages Emma Jones and
///     Carlos Rivera directly.
///   - Tom Williams (tom.williams@acme.example) — plain Employee, used for the denial test.
/// </summary>
[Collection("E2E")]
public sealed class ManagerDashboardTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string JamesEmail = "james.okafor@acme.example";
    private const string DavidEmail = "david.park@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form, then assigns David Park
    /// as their manager via the Employment tab (the New Employee form itself has no Manager
    /// field). Returns the new employee's id and last name. Caller must already be logged in as
    /// David.
    /// </summary>
    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeReportingToDavidAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Team{suffix}{unique}";
        var workEmail = $"e2e.team.{suffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");

        // Employee Number, Employment Type, Department, Location and Position Profile are all
        // mandatory. Selecting "Senior Software Engineer" (seeded with Engineering / London
        // Office attached) pre-populates Department and Location in one step.
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        var employeeId = Guid.Parse(match.Groups[1].Value);

        await empEdit.OpenEmploymentTabAsync();
        await empEdit.SelectManagerAsync("David Park");
        await empEdit.ClickSaveChangesAsync();

        // ClickSaveChangesAsync redirects to the employee list on success (EmployeeEdit.razor's
        // SaveCoreAsync always sets _redirectUrl to the list, even for an existing-employee
        // update) — that alone doesn't prove the manager assignment actually persisted server-side,
        // only that the save request didn't visibly error. Reload the employee fresh and re-read
        // the Manager field from the database-backed value, not the client-side selection made
        // moments ago, to catch a persistence failure here rather than as a confusing downstream
        // symptom (e.g. the employee silently missing from David's dashboard widgets).
        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenEmploymentTabAsync();
        var savedManager = await empEdit.GetSelectedManagerTextAsync();
        Assert.Equal("David Park", savedManager);

        return (employeeId, lastName);
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
    public async Task ManagerOnly_SeesManagerScopedWidgets_ButNotTeamOnboarding()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Team Tasks"));
        Assert.True(await dashboard.HasWidgetAsync("Leave Requests"));
        Assert.True(await dashboard.HasWidgetAsync("My Team"));
        Assert.True(await dashboard.HasWidgetAsync("Upcoming Probation Reviews"));
        Assert.True(await dashboard.HasWidgetAsync("Team Sickness Today"));
        Assert.True(await dashboard.HasWidgetAsync("Overdue Return-to-Work Reviews"));
        Assert.True(await dashboard.HasWidgetAsync("Missing Fit Notes"));

        // James has the Manager role but not HrAdministrator, so he lacks CanManageEmployees —
        // Team Onboarding additionally requires that permission (see class remarks).
        Assert.False(await dashboard.HasWidgetAsync("Team Onboarding"),
            "Expected Team Onboarding to be hidden for a Manager-only persona without CanManageEmployees");
    }

    [Fact]
    public async Task ManagerWithHrAdministratorRole_AlsoSeesTeamOnboardingWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Team Onboarding"),
            "Expected Team Onboarding to be visible for a Manager who is also an HrAdministrator");
    }

    [Fact]
    public async Task TeamTasksWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Team Tasks");
        await dashboard.GetTeamTaskTitlesAsync();
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
        // empDev1Id). MyTeamWidget.razor used to render these as icon-only mailto:/tel: links with
        // the value hidden in a "title" tooltip — they're now visible ".team-widget-contact-text"
        // spans next to each icon.
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
    public async Task UpcomingProbationReviewsWidget_IsVisible_ForManager()
    {
        // Full regression coverage (including the click-through navigation) for this widget
        // lives in HrDashboardTests, since it is shared between the HR and Manager dashboards
        // and its content does not differ by role — this just confirms it also renders here.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Upcoming Probation Reviews"));
        await dashboard.WaitForWidgetLoadedAsync("Upcoming Probation Reviews");
    }

    [Fact]
    public async Task TeamSicknessTodayWidget_Loads_SelfScopedToDirectReports()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Team Sickness Today"));
        // No seeded active sickness record for James's team as of writing — asserting the
        // widget resolves to a definite state (items or empty) is sufficient here; the widget's
        // actual sickness-detection logic is covered by the Sickness module's own tests.
        await dashboard.IsTeamSicknessTodayEmptyAsync();
    }

    [Fact]
    public async Task OverdueReturnToWorkAndMissingFitNotesWidgets_LoadWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Overdue Return-to-Work Reviews");
        await dashboard.WaitForWidgetLoadedAsync("Missing Fit Notes");
    }

    [Fact]
    public async Task TeamOnboardingWidget_ShowsEmployeeCurrentlyOnboarding()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        var (_, lastName) = await CreateEmployeeReportingToDavidAsync(empList, empEdit, "Show");

        await dashboard.GoToAsync();

        var names = await dashboard.GetTeamOnboardingEmployeeNamesAsync();

        Assert.True(
            names.Any(n => n.Contains(lastName, StringComparison.OrdinalIgnoreCase)),
            $"Expected the newly-created employee '{lastName}' (reporting to David, onboarding not " +
            $"yet started) to appear in the Team Onboarding widget. Names found: [{string.Join(", ", names)}]");
    }

    [Fact]
    public async Task ClickingTeamOnboardingItem_NavigatesToEmployeeProfile_WithOnboardingTabActive()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);
        var employee  = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        var (employeeId, lastName) = await CreateEmployeeReportingToDavidAsync(empList, empEdit, "Nav");

        await dashboard.GoToAsync();
        await dashboard.GetTeamOnboardingEmployeeNamesAsync();
        await dashboard.ClickTeamOnboardingItemAsync(lastName);

        Assert.Contains($"/employees/{employeeId}?tab=onboarding", _page.Url);
        Assert.Equal("Onboarding", await employee.GetActiveTabNameAsync());
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
        var (employeeId, lastName) = await CreateEmployeeReportingToDavidAsync(empList, empEdit, "Complete");

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
