using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Cross-cutting coverage for lifecycle tab visibility (Onboarding/Probation/Offboarding tabs on
/// the employee edit page) that spans more than one lifecycle module at once. Single-module
/// scenarios — tab visible while active, hidden once completed, hidden when no record ever
/// existed — are covered in EmployeeOnboardingTabTests.cs, EmployeeProbationTabTests.cs, and
/// EmployeeOffboardingTabTests.cs respectively.
/// </summary>
public sealed class EmployeeLifecycleTabVisibilityTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Sarah Chen — seeded CTO with no manager (EmployeesModule.SeedEmployeesAsync). Seeded
    // directly into the database rather than through CreateEmployeeHandler, so she never fired
    // the EmployeeCreated integration event: no onboarding plan, no probation record (no manager
    // to attach one to), and no offboarding plan has ever been started for her.
    private static readonly Guid SarahChen = Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Laura Bennett — HR Manager, logged in throughout; also who claims/completes offboarding
    // tasks in the completion test below.
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Drives the (already-open-via-OpenAsync) Start Leaving Process wizard end to end, exactly
    /// mirroring EmployeeLeavingProcessTests.StartLeavingProcessViaWizardAsync. Offboarding no
    /// longer has a manual trigger — it's only ever started as a side effect of confirming this
    /// wizard (StartLeavingProcessHandler calls IOffboardingPlanCoordinator.StartAsync
    /// internally), so this is used purely to reach an active-offboarding-plan state.
    /// </summary>
    private async Task StartLeavingProcessViaWizardAsync(
        StartLeavingProcessDialog dialog, string resignationDdMMyyyy, string reasonLabel)
    {
        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(resignationDdMMyyyy);
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw),
            "Expected step 2 to auto-populate a proposed leaving date from the employee's effective notice period");

        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync(reasonLabel);
        await dialog.ClickNextAsync();

        await dialog.ConfirmAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        // StartLeavingProcessDialog's OnCompleted callback force-navigates the parent page to
        // "?tab=leaving" — wait for the resulting full page reload to reconnect before the caller
        // reads anything else off the page.
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Lifecycle{suffix}{unique}";
        var workEmail = $"e2e.lifecycle.{suffix.ToLowerInvariant()}{unique}@acme.example";

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
        // mandatory. Selecting "QA Engineer" (seeded with Engineering / London
        // Office attached) pre-populates Department and Location in one step.
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task Employee_WithNoLifecycleProcesses_HidesAllThreeLifecycleTabs()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).IsVisibleAsync(),
            "Expected no 'Onboarding' tab for an employee who never had a plan");
        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).IsVisibleAsync(),
            "Expected no 'Probation' tab for an employee who never had a record");
        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected no 'Offboarding' tab for an employee who never had a plan");

        // There is no manual entry point to start offboarding anywhere in the UI anymore —
        // offboarding only ever starts as a side effect of the Start Leaving Process wizard.
        Assert.False(
            await _page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).IsVisibleAsync(),
            "Expected no manual 'Start Offboarding' entry point anywhere");
    }

    [Fact]
    public async Task Employee_WithMultipleActiveLifecycleProcesses_ShowsAllRelevantTabs()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Multi");

        // Freshly created: onboarding is auto-created (NotStarted) and immediately visible; no
        // manager was set on the New Employee form, so probation never gets created; offboarding
        // hasn't started yet.
        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).IsVisibleAsync(),
            "Expected the Onboarding tab to already be visible on a freshly created employee");
        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected no Offboarding tab yet");

        // Drive the Start Leaving Process wizard — offboarding only ever starts as a side effect
        // of it (StartLeavingProcessHandler calls IOffboardingPlanCoordinator.StartAsync
        // internally) — Onboarding and Offboarding should now both be visible at once for the
        // same employee.
        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).IsVisibleAsync(),
            "Expected the Onboarding tab to remain visible");
        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected the Offboarding tab to now also be visible");
    }

    [Fact]
    public async Task OffboardingTab_IsHidden_AfterCompletion()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var inbox       = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var profile     = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var taskView    = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, "OffComplete");

        // Offboarding no longer has a manual trigger — it only ever starts as a side effect of
        // confirming the Start Leaving Process wizard.
        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        Assert.True(await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected the Offboarding tab to be visible once started");

        // No manager and no assets, so StartOffboardingHandler generates exactly 5 unassigned
        // tasks: 1 HR document-review + 4 manager exit-checklist (see
        // StartOffboarding_ForEmployeeWithNoAssets_GeneratesExpectedFixedChecklistTasks in
        // EmployeeOffboardingTabTests.cs). Claim each from the HR Inbox, then complete it from
        // Laura's own My Profile Tasks tab (mirroring ProfileTasksTabTests.cs's View-button
        // flow) — there's no offboarding-specific dashboard widget the way Onboarding has one.
        //
        // "Review outstanding documents for employee exit" is a fixed title with no employee
        // name suffix (unlike the other four), so other tests in this collection that start
        // offboarding without completing it (e.g. StartOffboarding_WithValidLastWorkingDay_...
        // in EmployeeOffboardingTabTests.cs) can leave same-titled stray cards sitting unclaimed
        // in the same shared Acme HR Inbox. Draining every card that matches each fragment
        // (rather than assuming exactly one) guarantees ours gets claimed regardless of leftovers.
        string[] taskFragments =
        [
            "Review outstanding documents for employee exit",
            "Conduct exit interview",
            "Revoke system access and accounts",
            "Arrange handover and knowledge transfer",
            "Notify IT and Payroll of employee exit",
        ];

        foreach (var fragment in taskFragments)
        {
            await inbox.GoToAsync(AcmeId);
            var matchCount = (await inbox.GetTaskTitlesAsync())
                .Count(t => t.Contains(fragment, StringComparison.OrdinalIgnoreCase));

            for (var i = 0; i < matchCount; i++)
            {
                await inbox.GoToAsync(AcmeId);
                var titles = await inbox.GetTaskTitlesAsync();
                var claimedTitle = titles.First(t => t.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                await inbox.ClaimAsync(claimedTitle);

                await profile.GoToAsync(AcmeId, LauraId);
                await profile.OpenTasksTabAsync();

                // The Tasks tab shows every task ever assigned to Laura, completed ones included
                // (EmployeeTasksTab.razor applies no status filter) — since the HR document-review
                // title repeats across duplicate claims, excluding rows already marked "Completed"
                // picks out the one just claimed rather than an earlier, already-finished one.
                await _page.Locator(".e-row")
                    .Filter(new() { HasText = claimedTitle })
                    .Filter(new() { HasNotText = "Completed" })
                    .First
                    .Locator("button[title='View']")
                    .ClickAsync();

                await taskView.WaitForLoadedAsync();
                await taskView.CompleteGeneralTaskAsync();
                await taskView.CloseAsync();
            }
        }

        // Revisiting the employee's profile should no longer show an Offboarding tab. There is no
        // manual "Start Offboarding" entry point to reappear anymore — that button no longer
        // exists anywhere in the UI.
        await empEdit.GoToAsync(AcmeId, employeeId);

        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected the Offboarding tab to be hidden once the plan is Completed");
        Assert.False(
            await _page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).IsVisibleAsync(),
            "Expected no manual 'Start Offboarding' entry point anywhere");
    }
}
