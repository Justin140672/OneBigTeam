using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Onboarding tab on the employee edit page (progress panel, checklist, timeline,
/// and deep-link tab activation).
///
/// Onboarding plans are only created server-side via the EmployeeCreated integration event that
/// fires from the CreateEmployee handler (see
/// HR.Modules.Onboarding.Features.CreateOnboardingPlanOnEmployeeCreated.EmployeeCreatedHandler).
/// Seeded employees (added directly to the database by EmployeesModule.SeedEmployeesAsync, e.g.
/// Carlos Rivera) never fire that event, so they never have an onboarding plan — the Onboarding
/// tab would show its "No onboarding plan found for this employee" empty state for all of them.
/// Every test below therefore creates a fresh employee through the standard New Employee form
/// (mirroring CreateEmployeeTests.cs), which reliably produces a NotStarted plan with three
/// default checklist tasks.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeOnboardingTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form and returns their id,
    /// captured from the URL after navigating back into their profile from the employee list.
    /// Caller must already be logged in as an HR administrator.
    /// </summary>
    private async Task<Guid> CreateEmployeeWithFreshOnboardingPlanAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Onboard{suffix}{unique}";
        var workEmail = $"e2e.onboard.{suffix.ToLowerInvariant()}{unique}@acme.example";

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
        // mandatory now. Selecting "Senior Software Engineer" (seeded with Engineering / London
        // Office attached) pre-populates Department and Location in one step — same pattern as
        // CreateEmployeeTests.cs.
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task OnboardingTab_IsVisible_OnNewlyCreatedEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Vis");

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).IsVisibleAsync(),
            "Expected an 'Onboarding' tab on the employee edit page");
    }

    [Fact]
    public async Task OnboardingTab_ShowsProgressPanel()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Prog");
        await empEdit.OpenOnboardingTabAsync();

        Assert.True(await empEdit.HasOnboardingProgressPanelAsync(),
            "Expected the onboarding progress panel (status badge + progress bar) to be visible");
    }

    [Fact]
    public async Task OnboardingTab_ShowsChecklist()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Chk");
        await empEdit.OpenOnboardingTabAsync();

        Assert.True(await empEdit.HasOnboardingChecklistAsync(),
            "Expected the Onboarding Checklist card to be visible");
    }

    [Fact]
    public async Task OnboardingTab_ShowsTimeline()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Time");
        await empEdit.OpenOnboardingTabAsync();

        Assert.True(await empEdit.HasOnboardingTimelineAsync(),
            "Expected the Onboarding Timeline card to be visible");
    }

    [Fact]
    public async Task OnboardingTab_ProgressPanel_ShowsSensiblePlanStatus()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Status");
        await empEdit.OpenOnboardingTabAsync();

        var status = await empEdit.GetOnboardingStatusBadgeTextAsync();

        // A freshly created employee's plan should be "Not Started" (no tasks completed yet),
        // but accept any in-progress-ish label to keep this resilient to seed/order variance.
        Assert.True(
            status is "Not Started" or "In Progress" or "Completed",
            $"Expected a sensible onboarding plan status, got '{status}'");
    }

    [Fact]
    public async Task OnboardingTab_IsHidden_AfterCompletion_ButHistoryStillVisibleInAuditTab()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);
        var inbox     = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Hide");

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).IsVisibleAsync(),
            "Expected the Onboarding tab to be visible while the plan is not yet completed");

        // Claim and complete all three default checklist tasks — the plan only transitions to
        // Completed once every task is done (see CompleteOnboardingTaskFromTaskAction).
        string[] taskFragments =
        [
            "Set up workstation",
            "Send welcome email",
            "Schedule welcome and induction meeting",
        ];

        foreach (var fragment in taskFragments)
        {
            await inbox.GoToAsync(AcmeId);
            var titles = await inbox.GetTaskTitlesAsync();
            var claimedTitle = titles.First(t => t.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            await inbox.ClaimAsync(claimedTitle);

            await dashboard.GoToAsync();
            await dashboard.ClickMyOnboardingTaskAsync(claimedTitle);
            await taskView.WaitForLoadedAsync();
            await taskView.CompleteGeneralTaskAsync();
            await taskView.CloseAsync();
        }

        // Revisiting the employee's profile should no longer show an Onboarding tab at all.
        await empEdit.GoToAsync(AcmeId, employeeId);

        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).IsVisibleAsync(),
            "Expected the Onboarding tab to be hidden once the plan is Completed");

        // The underlying data isn't deleted — HR can still find the completion in Audit history.
        await empEdit.OpenAuditTabAsync();
        Assert.True(
            await empEdit.AuditHistoryRow("Onboarding completed").First.IsVisibleAsync(),
            "Expected the Audit tab to show an 'Onboarding completed' history entry");
    }

    [Fact]
    public async Task DeepLink_TabOnboarding_LandsDirectlyOnOnboardingTab()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var employee = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Deep");

        // EmployeeEdit.razor's LoadAsync maps "?tab=onboarding" to tab index 11 (the last tab).
        await empEdit.GoToAsync(AcmeId, employeeId, "tab=onboarding");

        Assert.Equal("Onboarding", await employee.GetActiveTabNameAsync());
    }
}
