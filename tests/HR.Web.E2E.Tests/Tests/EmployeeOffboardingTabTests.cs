using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Offboarding tab on the employee edit page: it's hidden entirely until an
/// offboarding process exists (see EmployeeEdit.razor's _showOffboardingTab), the resulting
/// progress panel / checklist, and deep-link tab activation.
///
/// Unlike Onboarding (auto-created via a domain event when an employee is created), an
/// Offboarding plan is now only ever created as a side effect of the "Start Leaving Process"
/// wizard — StartLeavingProcessHandler calls IOffboardingPlanCoordinator.StartAsync internally
/// once a leaving process is confirmed. There is no longer any direct manual trigger for
/// offboarding anywhere in the UI (the old Employee Overview header "Start Offboarding" button
/// and this tab's own empty-state button + dialog have both been removed), and deep-linking to
/// "?tab=offboarding" no longer force-shows the tab for an employee with no plan — it only lands
/// there once the server already reports the tab as visible. Every test below therefore creates a
/// fresh employee through the standard New Employee form (mirroring
/// EmployeeOnboardingTabTests.cs's CreateEmployeeWithFreshOnboardingPlanAsync) and, where an
/// active plan is needed, drives them through the Start Leaving Process wizard
/// (StartLeavingProcessDialog) exactly as EmployeeLeavingProcessTests.cs does.
/// </summary>
public sealed class EmployeeOffboardingTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form and returns their id,
    /// captured from the URL after navigating back into their profile from the employee list.
    /// Caller must already be logged in as an HR administrator. The employee has no assigned
    /// assets and no offboarding plan yet.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Offboard{suffix}{unique}";
        var workEmail = $"e2e.offboard.{suffix.ToLowerInvariant()}{unique}@acme.example";

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
        // mandatory now. Selecting "QA Engineer" (seeded with Engineering / London
        // Office attached) pre-populates Department and Location in one step — same pattern as
        // CreateEmployeeTests.cs.
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// Drives the (already-open-via-OpenAsync) Start Leaving Process wizard end to end, exactly
    /// mirroring EmployeeLeavingProcessTests.StartLeavingProcessViaWizardAsync: fills the
    /// resignation date, reads back the auto-computed leaving date (reusing it verbatim as the
    /// last working day, since it must be on or before the leaving date regardless of this
    /// employee's actual effective notice period length), picks <paramref name="reasonLabel"/>,
    /// then confirms. Offboarding is started automatically as a side effect
    /// (StartLeavingProcessHandler calls IOffboardingPlanCoordinator.StartAsync internally) — this
    /// helper is used purely to reach that state, so unlike the leaving-process test's own
    /// version it doesn't bother asserting the confirmation summary.
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

        // Last working day must be on or before the leaving date — reuse the same date so this
        // stays valid regardless of the employee's actual effective notice period length.
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

    [Fact]
    public async Task OffboardingTab_IsHidden_OnNewlyCreatedEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Hidden");

        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).IsVisibleAsync(),
            "Expected no 'Offboarding' tab for an employee with no offboarding record");
        Assert.False(
            await _page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).IsVisibleAsync(),
            "Expected no manual 'Start Offboarding' entry point anywhere — offboarding now only starts as a side effect of Start Leaving Process");
    }

    [Fact]
    public async Task StartLeavingProcess_TriggersOffboarding_CreatesPlanAndShowsOverview_AndDeepLinkWorks()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var offboarding = new EmployeeOffboardingTab(_page);
        var employee    = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, "Create");

        // Offboarding no longer has a manual trigger — it's only ever started as a side effect of
        // confirming the Start Leaving Process wizard (StartLeavingProcessHandler calls
        // IOffboardingPlanCoordinator.StartAsync internally).
        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        await offboarding.OpenAsync();

        Assert.True(await offboarding.HasProgressPanelAsync(),
            "Expected the offboarding progress panel to be visible after the leaving process triggered a plan");
        Assert.True(await offboarding.HasChecklistCardAsync(),
            "Expected the Offboarding Checklist card to be visible after the leaving process triggered a plan");

        var status = await offboarding.GetStatusBadgeTextAsync();
        Assert.True(
            status is "Not Started" or "In Progress",
            $"Expected a sensible newly-started offboarding plan status, got '{status}'");

        // Deep-linking to "?tab=offboarding" no longer force-shows the tab for an employee with no
        // plan — it only lands there once the plan (and therefore the tab) already exists, which
        // is now the case here.
        await empEdit.GoToAsync(AcmeId, employeeId, "tab=offboarding");
        Assert.Equal("Offboarding", await employee.GetActiveTabNameAsync());
    }

    [Fact]
    public async Task StartOffboarding_ForEmployeeWithNoAssets_GeneratesExpectedFixedChecklistTasks()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var offboarding = new EmployeeOffboardingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // A freshly created employee has zero assigned assets, so StartOffboardingHandler
        // should generate exactly 5 tasks: 1 HR document-review task + 4 fixed manager
        // exit-checklist tasks (see StartOffboardingHandler.CreateDocumentReviewTaskAsync /
        // CreateManagerExitChecklistAsync).
        await CreateEmployeeAsync(empList, empEdit, "Tasks");

        await StartLeavingProcessViaWizardAsync(startDialog, "15/09/2026", "Resignation");

        await offboarding.OpenAsync();

        Assert.True(await offboarding.HasChecklistCardAsync(),
            "Expected the Offboarding Checklist card to be visible after the leaving process triggered a plan");

        // Fixed HR task title (StartOffboardingHandler.CreateDocumentReviewTaskAsync) — no
        // employee name interpolated, so the full title can be matched exactly.
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Review outstanding documents for employee exit"),
            "Expected the fixed HR document-review task to appear in the checklist");

        // Fixed manager exit-checklist task titles interpolate the employee's display name
        // (e.g. "Conduct exit interview — E2E OffboardTasks<unique>"), so match on the
        // stable title prefix only (StartOffboardingHandler.CreateManagerExitChecklistAsync).
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Conduct exit interview"),
            "Expected the fixed exit-interview task to appear in the checklist");
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Revoke system access and accounts"),
            "Expected the fixed access-revocation task to appear in the checklist");
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Arrange handover and knowledge transfer"),
            "Expected the fixed handover task to appear in the checklist");
        Assert.True(
            await offboarding.HasChecklistTaskAsync("Notify IT and Payroll of employee exit"),
            "Expected the fixed IT/Payroll notification task to appear in the checklist");
    }
}
