
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeOnboardingTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeWithFreshOnboardingPlanAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, int slot)
    {
        _ = empList;
        var seeded = SeededE2eEmployees.OnboardingTab[slot];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);
        return (seeded.EmployeeId, seeded.LastName);
    }

    private async Task<(string ProfileTitle, string TaskTitle)> CreateHrOwnedSingleTaskProfileAsync()
    {
        var unique       = Guid.NewGuid().ToString("N")[..8];
        var templateName = $"E2E Onb HrOnly {unique}";
        var taskTitle    = $"E2E HR Checklist {unique}";
        var profileTitle = $"E2E Onb Role {unique}";

        var templateEdit = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);
        var ppList       = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit       = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(templateName);
        await templateEdit.ClickAddTaskAsync();
        await templateEdit.FillTaskTitleAsync(taskTitle);
        await templateEdit.SelectFirstTaskAssignToAsync("HR");
        await templateEdit.SaveAsync();

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SelectOnboardingTemplateAsync(templateName);
        await ppEdit.SaveAsync();

        return (profileTitle, taskTitle);
    }

    /// <summary>
    /// Creates a brand-new Acme employee through the "New Employee" UI. EmployeeCreatedHandler
    /// then provisions an onboarding plan from the template linked to the chosen position profile.
    /// Used by tests that must not mutate a shared pool employee.
    /// </summary>
    private async Task<(Guid EmployeeId, string LastName)> CreateGenuinelyFreshEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string positionProfile)
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"OnbFresh{unique}";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync($"e2e.onbfresh{unique}@acme.example");
        await empEdit.FillRequiredAddressAsync();
        await empEdit.FillRequiredCompensationAsync();
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        // Deliberately EARLIER than the E2E pool's 2026-03-01 start date (and every other test's
        // new-employee start date). The HR Inbox (GetUnassignedTasks) returns at most 200 tasks
        // ordered by priority, then due date, then created-at, and onboarding task due dates derive
        // from the start date. The seeded pool alone contributes ~190 unassigned onboarding tasks
        // due 2026-03-01/08, so a fresh employee sharing that start date sorted behind all of them
        // (later CreatedAt) and fell past the 200 cap — this test then found no matching inbox
        // card. An earlier due date keeps this employee's tasks inside the returned window.
        await empEdit.FillStartDateAsync("01/01/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-ONB-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", positionProfile);
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(System.Text.RegularExpressions.Regex.Match(
            _page.Url, @"/employees/([0-9a-fA-F-]{36})").Groups[1].Value);

        return (employeeId, lastName);
    }

    [Fact]
    public async Task OnboardingTab_IsVisible_OnNewlyCreatedEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);

        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Onboarding"),
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

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);
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

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);
        await empEdit.OpenOnboardingTabAsync();

        Assert.True(await empEdit.HasOnboardingChecklistAsync(),
            "Expected the Onboarding Checklist card to be visible");
    }

    [Fact]
    public async Task OnboardingTab_DoesNotShowTimeline()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);
        await empEdit.OpenOnboardingTabAsync();

        Assert.True(await empEdit.HasOnboardingChecklistAsync(),
            "Expected the Onboarding Checklist card to be visible");
        Assert.False(await empEdit.HasOnboardingTimelineAsync(),
            "Expected the Onboarding Timeline card to be removed");
    }

    [Fact]
    public async Task OnboardingTab_Checklist_ShowsOwner_AndOpensTaskDialog_WithoutEmployeeNameSuffix()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, lastName) = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);
        await empEdit.OpenOnboardingTabAsync();

        const string workstationTitle = "Prepare workstation, equipment, accounts, and system access";
        var rowTitle = await empEdit.GetOnboardingChecklistRowTitleAsync(workstationTitle);
        Assert.NotNull(rowTitle);
        Assert.DoesNotContain(lastName, rowTitle!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Unassigned", await empEdit.GetOnboardingChecklistOwnerAsync(workstationTitle));

        var urlBefore = _page.Url;
        await empEdit.OpenOnboardingChecklistTaskAsync(workstationTitle);
        Assert.Equal(urlBefore, _page.Url);
    }

    [Fact]
    public async Task EmployeeTasksTab_IncludesOnboardingTasks_WithoutEmployeeNameSuffix()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (_, lastName) = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);
        await empEdit.OpenTasksTabAsync();

        var workstationTask = _page.Locator(".e-row").Filter(new() { HasText = "Prepare workstation, equipment, accounts, and system access" }).First;
        await workstationTask.WaitForAsync(new() { Timeout = 15_000 });
        var title = (await workstationTask.Locator(".task-title").TextContentAsync())!;
        Assert.DoesNotContain(lastName, title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnboardingTab_ProgressPanel_ShowsSensiblePlanStatus()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);
        await empEdit.OpenOnboardingTabAsync();

        var status = await empEdit.GetOnboardingStatusBadgeTextAsync();

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

        var (profileTitle, taskTitle) = await CreateHrOwnedSingleTaskProfileAsync();
        var (employeeId, lastName) = await CreateGenuinelyFreshEmployeeAsync(empList, empEdit, profileTitle);

        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Onboarding"),
            "Expected the Onboarding tab to be visible while the plan is not yet completed");

        string[] taskFragments = [taskTitle];

        foreach (var fragment in taskFragments)
        {
            await inbox.GoToAsync(AcmeId, lastName);
            var titles = await inbox.GetTaskTitlesAsync();
            var claimedTitle = titles.First(t =>
                t.Contains(fragment, StringComparison.OrdinalIgnoreCase) &&
                t.Contains(lastName, StringComparison.OrdinalIgnoreCase));
            await inbox.ClaimAsync(claimedTitle);

            await profile.GoToAsync(AcmeId, LauraId);
            await profile.OpenTasksTabAsync();
            await profile.ClickTaskAsync(claimedTitle);
            await taskView.WaitForLoadedAsync();
            await taskView.CompleteGeneralTaskAsync();
            await taskView.CloseAsync();
        }

        await empEdit.GoToAsync(AcmeId, employeeId);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Onboarding");
        await Assertions.Expect(EmployeeEditPage.SectionTab(_page, "Onboarding"))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        await empEdit.OpenAuditTabAsync();

        await Assertions.Expect(empEdit.AuditHistoryRow("Onboarding completed").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task HrOwnedOnboardingTask_ForNewEmployee_AppearsInHrInbox_AsAssignedToHr_AndInChecklistAsHr()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var inbox   = new HrInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (profileTitle, taskTitle) = await CreateHrOwnedSingleTaskProfileAsync();
        var (employeeId, lastName) = await CreateGenuinelyFreshEmployeeAsync(empList, empEdit, profileTitle);

        await inbox.GoToAsync(AcmeId, lastName);
        var inboxTitle = (await inbox.GetTaskTitlesAsync()).First(t =>
            t.Contains(taskTitle, StringComparison.OrdinalIgnoreCase) &&
            t.Contains(lastName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Assigned to HR", await inbox.GetOwnerBadgeTextAsync(inboxTitle));

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenOnboardingTabAsync();
        Assert.Equal("HR", await empEdit.GetOnboardingChecklistOwnerAsync(taskTitle));
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

        var (employeeId, _) = await CreateEmployeeWithFreshOnboardingPlanAsync(empList, empEdit, slot: 0);

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=onboarding");

        Assert.Equal("Onboarding", await employee.GetActiveTabNameAsync());
    }
}
