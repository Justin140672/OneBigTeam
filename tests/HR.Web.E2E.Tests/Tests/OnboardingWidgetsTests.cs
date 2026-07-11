using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "My Onboarding Tasks" and "Team Onboarding" dashboard widgets.
///
/// As with EmployeeOnboardingTabTests, onboarding plans only exist for employees created through
/// the CreateEmployee handler (which fires the EmployeeCreated integration event picked up by
/// HR.Modules.Onboarding.Features.CreateOnboardingPlanOnEmployeeCreated.EmployeeCreatedHandler) —
/// no seeded employee has one, so tests that need onboarding data create a fresh employee first.
///
/// Laura Bennett (laura.bennett@acme.example) is used throughout: she is an HR Administrator
/// with CanManageEmployees, so both onboarding widgets are visible to her, and she can be
/// assigned as a new employee's manager to populate the Team Onboarding widget (which is scoped
/// to the current user's direct reports — see HR.Modules.Onboarding.Features.GetTeamOnboarding).
/// </summary>
[Collection("E2E")]
public sealed class OnboardingWidgetsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form, then assigns Laura
    /// Bennett as their manager via the Employment tab (the New Employee form itself has no
    /// Manager field). Returns the new employee's id and last name. Caller must already be
    /// logged in as Laura.
    /// </summary>
    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeReportingToLauraAsync(
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

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        var employeeId = Guid.Parse(match.Groups[1].Value);

        // The New Employee form doesn't collect Employee Number / Employment Type, but both are
        // required on the Employment tab's own EditContext — they must be filled here too, or
        // saving the Manager selection alongside them will fail client-side validation.
        await empEdit.OpenEmploymentTabAsync();
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectManagerAsync("Laura Bennett");
        await empEdit.ClickSaveChangesAsync();

        return (employeeId, lastName);
    }

    [Fact]
    public async Task Dashboard_ShowsMyOnboardingTasksWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasMyOnboardingTasksWidgetAsync(),
            "Expected the 'My Onboarding Tasks' widget to be visible on the dashboard");
    }

    [Fact]
    public async Task Dashboard_ShowsTeamOnboardingWidget_ForManagerAccount()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasTeamOnboardingWidgetAsync(),
            "Expected the 'Team Onboarding' widget to be visible for a manager account with CanManageEmployees");
    }

    [Fact]
    public async Task TeamOnboardingWidget_ShowsEmployeeCurrentlyOnboarding()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, lastName) = await CreateEmployeeReportingToLauraAsync(empList, empEdit, "Show");

        await dashboard.GoToAsync();

        var names = await dashboard.GetTeamOnboardingEmployeeNamesAsync();

        Assert.True(
            names.Any(n => n.Contains(lastName, StringComparison.OrdinalIgnoreCase)),
            $"Expected the newly-created employee '{lastName}' (reporting to Laura, onboarding not " +
            $"yet started) to appear in the Team Onboarding widget. Names found: [{string.Join(", ", names)}]");
    }

    [Fact]
    public async Task ClickingTeamOnboardingItem_NavigatesToEmployeeProfile_WithOnboardingTabActive()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);
        var employee  = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, lastName) = await CreateEmployeeReportingToLauraAsync(empList, empEdit, "Nav");

        await dashboard.GoToAsync();
        await dashboard.GetTeamOnboardingEmployeeNamesAsync();

        await _page.Locator(".widget-card")
            .Filter(new() { HasText = "Team Onboarding" })
            .Locator(".task-widget-item")
            .Filter(new() { HasText = lastName })
            .First
            .ClickAsync();

        await _page.WaitForURLAsync(new Regex($@"/employees/{Regex.Escape(employeeId.ToString())}\?tab=onboarding"),
            new() { Timeout = 15_000 });

        Assert.Equal("Onboarding", await employee.GetActiveTabNameAsync());
    }

    /// <summary>
    /// Cross-cutting completion flow: an onboarding-sourced task is completed through the
    /// existing Task view dialog (not any new UI added by this feature set), and that completion
    /// is reflected both in the "My Onboarding Tasks" widget (the task disappears) and on the
    /// employee's Onboarding tab (progress increases, the task's checklist row shows Completed,
    /// and the plan status moves from "Not Started" to "In Progress").
    ///
    /// The default onboarding checklist tasks created for a brand-new employee are all
    /// unassigned at creation time (the New Employee form has no Manager field, and the
    /// "assign to new hire" template path isn't exercised by the default fallback checklist —
    /// see CreateOnboardingPlanOnEmployeeCreated.EmployeeCreatedHandler), so they land in the HR
    /// Inbox as unassigned tasks. Laura claims one from there (the same self-assign flow covered
    /// by HrInboxTests), which is what makes it appear in her own "My Onboarding Tasks" widget
    /// for her to complete.
    /// </summary>
    [Fact]
    public async Task CompletingOnboardingTask_RemovesItFromMyOnboardingTasksWidget_AndUpdatesOnboardingTabProgress()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);
        var inbox     = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 1: create a new employee — auto-creates an onboarding plan with three
        // default checklist tasks, all unassigned (see class remarks above). ──────────────
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Complete{unique}";
        var workEmail = $"e2e.onboard.complete{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        var employeeId = Guid.Parse(match.Groups[1].Value);

        // ── Step 2: claim one of the new employee's unassigned onboarding tasks from the
        // HR Inbox — this assigns it to Laura, making it appear in her own widget. ──────────
        await inbox.GoToAsync(AcmeId);
        var inboxTitles  = await inbox.GetTaskTitlesAsync();
        var claimedTitle = inboxTitles.First(t => t.Contains(lastName, StringComparison.OrdinalIgnoreCase));
        await inbox.ClaimAsync(claimedTitle);

        // ── Step 3: the claimed task now appears in Laura's My Onboarding Tasks widget. ─────
        await dashboard.GoToAsync();
        var widgetTitlesBefore = await dashboard.GetMyOnboardingTaskTitlesAsync();
        Assert.Contains(widgetTitlesBefore, t => t.Contains(claimedTitle, StringComparison.OrdinalIgnoreCase));

        // ── Step 4: complete it via the existing Task view dialog flow. ─────────────────────
        await dashboard.ClickMyOnboardingTaskAsync(claimedTitle);
        await taskView.WaitForLoadedAsync();
        await taskView.CompleteGeneralTaskAsync();
        await taskView.CloseAsync();

        // ── Step 5: it disappears from the My Onboarding Tasks widget. ──────────────────────
        await dashboard.GoToAsync();
        var widgetTitlesAfter = await dashboard.GetMyOnboardingTaskTitlesAsync();
        Assert.DoesNotContain(widgetTitlesAfter, t => t.Contains(claimedTitle, StringComparison.OrdinalIgnoreCase));

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
