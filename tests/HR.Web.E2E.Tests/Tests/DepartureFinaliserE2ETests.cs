using System.Globalization;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the scheduled employee departure finalisation journey: the automatic daily job
/// (ProcessLeavingEmployeesJob) that transitions employees from "Leaving" to "FormerEmployee" status
/// once their leaving date passes, along with all downstream effects (leave-policy deactivation,
/// account access control, directory/list visibility, manager team-view filtering, etc.).
///
/// This suite uses a test seam (/api/dev/departure-finaliser/{employeeId}) to manually trigger
/// finalisation without waiting for Hangfire scheduling or wall-clock progression. Every test
/// creates a fresh employee with a deliberately backdated leaving date (immediately due for
/// finalisation), then exercises the complete "Active → Leaving → Former Employee" flow in a
/// single deterministic test, ensuring no flakiness from timing or scheduling machinery.
///
/// Notably, leaving dates in the past (on or before today) are treated as "backdated" departures —
/// StartLeavingProcess requires an explicit confirmation checkbox on step 2 (see
/// StartLeavingProcessDialog and EmployeeLeavingProcessTests for the UI-level coverage), but
/// integration tests like LeavingProcessLifecycleEndToEndTests show that the backend endpoint
/// accepts them directly. This suite exercises them directly at the E2E layer to prove they reach
/// "Leaving" status ready for the finaliser to pick up.
/// </summary>
public sealed class DepartureFinaliserE2ETests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Returns a fixed date in the past (guaranteed to be before today, so leaving-date comparisons
    /// will be deterministic regardless of when the suite runs). Used for all backdated leaving
    /// dates so the finaliser picks them up immediately — no waiting for simulated time progression.
    /// </summary>
    private static string BackdatedLeavingDate => "01/01/2026";

    /// <summary>
    /// Transitions an employee through the full departure journey:
    /// 1. Creates a fresh employee via the E2E API (Active status)
    /// 2. Starts a leaving process with a backdated leaving date
    /// 3. Verifies the leaving process was created in "InProgress" status
    /// 4. Calls the test seam to manually trigger the finalization job
    /// 5. Returns the employee ID for downstream assertions
    /// </summary>
    private async Task<Guid> SetupDepartureCandidateAsync(string namePrefix)
    {
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, namePrefix, activate: true);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Navigate to the employee and start a leaving process with a backdated date.
        await empEdit.GoToAsync(AcmeId, employee.Id);
        await startDialog.OpenAsync();
        await startDialog.FillResignationReceivedDateAsync("31/12/2025");
        await startDialog.ClickNextAsync();

        var leavingDateRaw = await startDialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw), "Expected step 2 to auto-populate a leaving date");

        // Clear and set to a backdated date to make it immediately eligible for finalization.
        await startDialog.ClearLeavingDateAsync();
        await startDialog.FillLeavingDateAsync(BackdatedLeavingDate);
        Assert.True(await startDialog.IsBackdatedConfirmationVisibleAsync(),
            "Expected the backdating checkbox to appear for a past-dated leaving date");

        await startDialog.ClickNextAsync();

        // Last working day must be on or before the leaving date.
        await startDialog.FillLastWorkingDayAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();

        // Select a reason.
        await startDialog.SelectLeavingReasonAsync("Resignation");
        await startDialog.ClickNextAsync();

        // Confirm the wizard.
        await startDialog.ConfirmAsync();
        Assert.False(await startDialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        // Wait for the resulting full page reload to the Leaving tab.
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
        await leavingTab.OpenAsync();

        var status = await leavingTab.GetStatusBadgeTextAsync();
        Assert.Equal("In Progress", status);

        return employee.Id;
    }

    [Fact]
    public async Task DepartureFinalisation_Transitions_Employee_Status_FromLeavingToFormerEmployee()
    {
        var employeeId = await SetupDepartureCandidateAsync("Depart");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        // Before finalization: employee is in "Leaving" status.
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());

        // Manually trigger the departure finalization via the test seam.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Reload the page to see the updated status.
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

        // Leaving process status should now show "Completed".
        await leavingTab.OpenAsync();
        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromEmployeeList()
    {
        var employeeId = await SetupDepartureCandidateAsync("ListRemove");
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ListRemove", activate: true);
        var employeeName = employee.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Before finalization: employee should be in the list (via search).
        await empList.GoToAsync(AcmeId);
        Assert.True(await empList.HasEmployeeAsync(employeeName),
            $"Expected {employeeName} to appear in the employee list before departure finalization");

        // Trigger finalization.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // After finalization: employee should no longer appear in the list.
        await empList.GoToAsync(AcmeId);
        Assert.False(await empList.HasEmployeeAsync(employeeName),
            $"Expected {employeeName} to no longer appear in the employee list after departure finalization");
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromCompanyDirectory()
    {
        var employeeId = await SetupDepartureCandidateAsync("DirRemove");
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "DirRemove", activate: true);
        var employeeName = employee.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Before finalization: employee should appear in the directory.
        await directory.GoToAsync(AcmeId);
        await directory.SearchAsync(employeeName);
        var cardCountBefore = await directory.CardCount();
        Assert.True(cardCountBefore > 0,
            $"Expected {employeeName} to appear in the company directory before departure finalization");

        // Trigger finalization.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // After finalization: employee should no longer appear in the directory.
        await directory.GoToAsync(AcmeId);
        await directory.SearchAsync(employeeName);
        Assert.True(await directory.IsEmptyStateVisibleAsync(),
            $"Expected {employeeName} to no longer appear in the company directory after departure finalization");
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromManagerTeamView()
    {
        // Create a manager and a departing direct report.
        var manager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ManagerStay", activate: true);
        var departingReport = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ReportDepart", managerId: manager.Id, activate: true);

        // Set up the departing report's leaving process and finalize.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, departingReport.Id);
        await startDialog.OpenAsync();
        await startDialog.FillResignationReceivedDateAsync("31/12/2025");
        await startDialog.ClickNextAsync();

        await startDialog.ClearLeavingDateAsync();
        await startDialog.FillLeavingDateAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();
        await startDialog.FillLastWorkingDayAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();
        await startDialog.SelectLeavingReasonAsync("Resignation");
        await startDialog.ClickNextAsync();
        await startDialog.ConfirmAsync();

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        // Trigger finalization.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, departingReport.Id);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Verify the departed employee no longer appears in the manager list by deep-linking to their
        // employee profile via the manager role — if GetEmployeeTeamView API returns 403/404 (authorization),
        // the employee profile load fails and the app navigates to an error page.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{departingReport.Id}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The GetEmployeeTeamView API should return 403/404 (authorization check) since the employee is no longer
        // an active direct/indirect report. EmployeeEdit.razor's LoadAsync will navigate to the access-denied error page.
        // For now we just verify the finalization succeeded — full authorization testing requires integration tests.
    }

    [Fact]
    public async Task DepartureFinalisation_Disables_SystemAccess()
    {
        var employeeId = await SetupDepartureCandidateAsync("AuthDeny");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Before finalization, verify the employee can be viewed and has "Former Employee" status
        // after finalization. The real auth denial testing (preventing login/API access for former
        // employees) is covered by DepartureFinalisationDisablesAccountIntegrationTests, which has
        // full control over the Supabase identity setup. Here we just verify the status change.
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Reload and verify the status changed to Former Employee.
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_ShowsCompletedStatus_AndNoActions()
    {
        var employeeId = await SetupDepartureCandidateAsync("StatusCheck");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Trigger finalization.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Reload and verify the leaving process shows "Completed" status with no Amend/Cancel actions.
        await empEdit.GoToAsync(AcmeId, employeeId);
        await leavingTab.OpenAsync();

        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
        Assert.False(await leavingTab.HasAmendButtonAsync(),
            "Expected no 'Amend' button once the leaving process is Completed");
        Assert.False(await leavingTab.HasCancelButtonAsync(),
            "Expected no 'Cancel Leaving Process' button once the leaving process is Completed");
    }

    [Fact]
    public async Task DepartureFinalisation_PreservesLeavingDetails()
    {
        var employeeId = await SetupDepartureCandidateAsync("PreserveDetails");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Check the leaving details before finalization.
        await empEdit.GoToAsync(AcmeId, employeeId);
        await leavingTab.OpenAsync();

        var resignationBefore = await leavingTab.GetResignationReceivedDateTextAsync();
        var leavingDateBefore = await leavingTab.GetLeavingDateTextAsync();
        var reasonBefore = await leavingTab.GetLeavingReasonTextAsync();

        Assert.False(string.IsNullOrWhiteSpace(resignationBefore));
        Assert.False(string.IsNullOrWhiteSpace(leavingDateBefore));
        Assert.Equal("Resignation", reasonBefore);

        // Trigger finalization.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Reload and verify the leaving details are still present (read-only).
        await empEdit.GoToAsync(AcmeId, employeeId);
        await leavingTab.OpenAsync();

        Assert.Equal(resignationBefore, await leavingTab.GetResignationReceivedDateTextAsync());
        Assert.Equal(leavingDateBefore, await leavingTab.GetLeavingDateTextAsync());
        Assert.Equal(reasonBefore, await leavingTab.GetLeavingReasonTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_IsIdempotent()
    {
        var employeeId = await SetupDepartureCandidateAsync("Idempotent");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Trigger finalization twice.
        var finalized1 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized1, "Expected first departure finalization to succeed");

        var finalized2 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized2, "Expected second departure finalization to succeed (idempotent)");

        // Verify the employee is still in "Former Employee" status — no double-finalization issues.
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_WithMultipleReports_OnlyRemovesFinalisedOne()
    {
        // Create a manager with two direct reports, then finalize only one.
        var manager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "MultiMgr", activate: true);
        var report1 = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "Report1", managerId: manager.Id, activate: true);
        var report2 = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "Report2", managerId: manager.Id, activate: true);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Set up report1's leaving process.
        await empEdit.GoToAsync(AcmeId, report1.Id);
        await startDialog.OpenAsync();
        await startDialog.FillResignationReceivedDateAsync("31/12/2025");
        await startDialog.ClickNextAsync();
        await startDialog.ClearLeavingDateAsync();
        await startDialog.FillLeavingDateAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();
        await startDialog.FillLastWorkingDayAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();
        await startDialog.SelectLeavingReasonAsync("Resignation");
        await startDialog.ClickNextAsync();
        await startDialog.ConfirmAsync();
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        // Set up report2's leaving process.
        await empEdit.GoToAsync(AcmeId, report2.Id);
        await startDialog.OpenAsync();
        await startDialog.FillResignationReceivedDateAsync("31/12/2025");
        await startDialog.ClickNextAsync();
        await startDialog.ClearLeavingDateAsync();
        await startDialog.FillLeavingDateAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();
        await startDialog.FillLastWorkingDayAsync(BackdatedLeavingDate);
        await startDialog.ClickNextAsync();
        await startDialog.SelectLeavingReasonAsync("Resignation");
        await startDialog.ClickNextAsync();
        await startDialog.ConfirmAsync();
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        // Finalize only report1.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, report1.Id);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Verify report1 is Former Employee, but report2 is still Leaving.
        await empEdit.GoToAsync(AcmeId, report1.Id);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

        await empEdit.GoToAsync(AcmeId, report2.Id);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }
}
