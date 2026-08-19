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
public sealed class EmployeeOnboardingTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form and returns their id,
    /// captured from the URL after navigating back into their profile from the employee list.
    /// Caller must already be logged in as an HR administrator.
    /// </summary>
    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeWithFreshOnboardingPlanAsync(
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
        return (Guid.Parse(match.Groups[1].Value), lastName);
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
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var inbox     = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, lastName) = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Hide");

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
            // Every onboarding task title is suffixed "— {FirstName LastName}" (see
            // EmployeeCreatedHandler), and this Inbox is shared across the whole E2E run/DB — it
            // can accumulate other employees' still-unclaimed "Set up workstation…" etc. tasks
            // from other tests. Matching on the generic fragment alone can grab a DIFFERENT
            // employee's task (whichever sorts first), silently completing it while this
            // employee's own task is never touched — and the plan then never reaches Completed,
            // since CompleteOnboardingTaskFromTaskAction requires every one of THIS plan's tasks
            // done. Disambiguate with this employee's own last name.
            var claimedTitle = titles.First(t =>
                t.Contains(fragment, StringComparison.OrdinalIgnoreCase) &&
                t.Contains(lastName, StringComparison.OrdinalIgnoreCase));
            await inbox.ClaimAsync(claimedTitle);

            // MyOnboardingTasksWidget (the old dashboard widget this used to click through) is
            // dead code — no longer rendered anywhere. Laura's own profile Tasks tab is the
            // current, role-agnostic place to find and open a task she has claimed.
            await profile.GoToAsync(AcmeId, LauraId);
            await profile.OpenTasksTabAsync();
            await profile.ClickTaskAsync(claimedTitle);
            await taskView.WaitForLoadedAsync();
            await taskView.CompleteGeneralTaskAsync();
            await taskView.CloseAsync();
        }

        // Revisiting the employee's profile should no longer show an Onboarding tab at all.
        await empEdit.GoToAsync(AcmeId, employeeId);

        // Same race documented on HasNotesTabAsync/HasProfilePhotoInitialsAsync in
        // EmployeeEditPage.cs: GoToAsync's own wait condition (the Details tab's combobox) can
        // resolve on an earlier render pass than the Onboarding tab's own visibility, which
        // depends on its own async plan-status load — a bare IsVisibleAsync() snapshot right after
        // navigation can catch that transient state instead of the settled (hidden) one. Use an
        // auto-retrying negative assertion instead of a one-shot check.
        await Assertions.Expect(_page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The underlying data isn't deleted — HR can still find the completion in Audit history.
        await empEdit.OpenAuditTabAsync();

        // OpenAuditTabAsync's own wait only proves the grid container (or its empty-state
        // sibling) attached — same class of race as the Onboarding-tab check above: the audit
        // history rows themselves populate via a separate, later async load, so a bare
        // IsVisibleAsync() snapshot right after the tab click can catch that transient
        // (row-not-yet-rendered) state instead of the settled one. Use an auto-retrying assertion.
        await Assertions.Expect(empEdit.AuditHistoryRow("Onboarding completed").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
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

        var (employeeId, _) = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, "Deep");

        // EmployeeEdit.razor's LoadAsync maps "?tab=onboarding" to tab index 11 (the last tab).
        await empEdit.GoToAsync(AcmeId, employeeId, "tab=onboarding");

        Assert.Equal("Onboarding", await employee.GetActiveTabNameAsync());
    }
}
