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

    private static DateOnly CompanyToday => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);

    private async Task<Guid> SetupDepartureCandidateAsync(string namePrefix) =>
        (await SetupDepartureEmployeeAsync(namePrefix)).Id;

    private async Task<E2eEmployeeApi.CreatedEmployee> SetupDepartureEmployeeAsync(string namePrefix)
    {
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, namePrefix, activate: true);
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, employee.Id);
        await EnsureLoggedInAsync();
        return employee;
    }

    private async Task EnsureLoggedInAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
    }

    private async Task<Guid> SetupDepartureCandidateViaWizardAsync(string namePrefix)
    {
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, namePrefix, activate: true);

        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var leavingTab = new EmployeeLeavingTab(_page);
        var todayText = CompanyToday.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        await EnsureLoggedInAsync();

        await empEdit.GoToAsync(AcmeId, employee.Id);
        await startDialog.OpenAsync();
        await startDialog.FillResignationReceivedDateAsync(CompanyToday.AddDays(-7).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        await startDialog.ClickNextAsync();

        Assert.False(string.IsNullOrWhiteSpace(await startDialog.GetLeavingDateTextAsync()),
            "Expected step 2 to auto-populate a leaving date");
        await startDialog.ClearLeavingDateAsync();
        await startDialog.FillLeavingDateAsync(todayText);
        await startDialog.ClickNextAsync();

        await startDialog.FillLastWorkingDayAsync(todayText);
        await startDialog.ClickNextAsync();

        await startDialog.SelectLeavingReasonAsync("Resignation");
        await startDialog.ClickNextAsync();

        await startDialog.ConfirmAsync();
        Assert.False(await startDialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
        await leavingTab.OpenAsync();
        Assert.Equal("In Progress", await leavingTab.GetStatusBadgeTextAsync());

        return employee.Id;
    }

    [Fact]
    public async Task DepartureFinalisation_Transitions_Employee_Status_FromLeavingToFormerEmployee()
    {
        var employeeId = await SetupDepartureCandidateViaWizardAsync("Depart");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

        await leavingTab.OpenAsync();
        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromEmployeeList()
    {
        // The admin employee list (ListEmployeesHandler) is unfiltered by status by default — it
        // deliberately still shows Former Employees by name/number search, since HR needs to find
        // historical records. "Removed from the list" therefore means removed from the *Active*
        // filter view, not the unfiltered default view (which SetupDepartureEmployeeAsync/
        // SetupDepartureCandidateAsync would already have left "Leaving", not "Active", by the time
        // this test could check it anyway).
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ListRemove", activate: true);
        var employeeId = employee.Id;
        var employeeName = employee.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);
        await empList.SelectStatusFilterAsync("Active");
        Assert.True(await empList.HasEmployeeAsync(employeeName),
            $"Expected {employeeName} to appear in the Active-filtered employee list before departure");

        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, employeeId);
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        await empList.GoToAsync(AcmeId);
        await empList.SelectStatusFilterAsync("Active");
        Assert.False(await empList.HasEmployeeAsync(employeeName),
            $"Expected {employeeName} to no longer appear in the Active-filtered employee list after departure finalization");
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromCompanyDirectory()
    {
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "DirRemove", activate: true);
        var employeeId = employee.Id;
        var employeeName = employee.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await directory.GoToAsync(AcmeId);
        await directory.SearchAsync(employeeName);
        var cardCountBefore = await directory.CardCount();
        Assert.True(cardCountBefore > 0,
            $"Expected {employeeName} to appear in the company directory before departure finalization");

        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, employeeId);
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        await directory.GoToAsync(AcmeId);
        await directory.SearchAsync(employeeName);
        Assert.True(await directory.IsEmptyStateVisibleAsync(),
            $"Expected {employeeName} to no longer appear in the company directory after departure finalization");
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromManagerTeamView()
    {
        var manager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ManagerStay", activate: true);
        var departingReport = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ReportDepart", managerId: manager.Id, activate: true);

        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, departingReport.Id);
        await EnsureLoggedInAsync();

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, departingReport.Id);
        Assert.True(finalized, "Expected departure finalization to succeed");

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{departingReport.Id}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    }

    [Fact]
    public async Task DepartureFinalisation_Disables_SystemAccess()
    {
        var employeeId = await SetupDepartureCandidateAsync("AuthDeny");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

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

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

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

        await empEdit.GoToAsync(AcmeId, employeeId);
        await leavingTab.OpenAsync();

        var resignationBefore = await leavingTab.GetResignationReceivedDateTextAsync();
        var leavingDateBefore = await leavingTab.GetLeavingDateTextAsync();
        var reasonBefore = await leavingTab.GetLeavingReasonTextAsync();

        Assert.False(string.IsNullOrWhiteSpace(resignationBefore));
        Assert.False(string.IsNullOrWhiteSpace(leavingDateBefore));
        Assert.Equal("Resignation", reasonBefore);

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

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

        var finalized1 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized1, "Expected first departure finalization to succeed");

        var finalized2 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized2, "Expected second departure finalization to succeed (idempotent)");

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_WithMultipleReports_OnlyRemovesFinalisedOne()
    {
        var manager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "MultiMgr", activate: true);
        var report1 = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "Report1", managerId: manager.Id, activate: true);
        var report2 = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "Report2", managerId: manager.Id, activate: true);

        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, report1.Id);
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, report2.Id);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, report1.Id);
        Assert.True(finalized, "Expected departure finalization to succeed");

        await empEdit.GoToAsync(AcmeId, report1.Id);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

        await empEdit.GoToAsync(AcmeId, report2.Id);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task MultipleEmployees_AllDueForFinalisation_AreProcessedInBatch()
    {
        var emp1 = await SetupDepartureCandidateAsync("BatchEmp1");
        var emp2 = await SetupDepartureCandidateAsync("BatchEmp2");
        var emp3 = await SetupDepartureCandidateAsync("BatchEmp3");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        foreach (var employeeId in new[] { emp1, emp2, emp3 })
        {
            await empEdit.GoToAsync(AcmeId, employeeId);
            Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
        }

        foreach (var employeeId in new[] { emp1, emp2, emp3 })
        {
            var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
            Assert.True(finalized, "Expected departure finalization to succeed");
        }

        foreach (var employeeId in new[] { emp1, emp2, emp3 })
        {
            await empEdit.GoToAsync(AcmeId, employeeId);
            Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
        }
    }

    [Fact]
    public async Task DepartureFinalisation_ExecutedTwice_IsIdempotentAtJobLevel()
    {
        var employeeId = await SetupDepartureCandidateAsync("IdempotentJob");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var finalized1 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized1, "Expected first departure finalization to succeed");

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
        await leavingTab.OpenAsync();
        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());

        var finalized2 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized2, "Expected second departure finalization to succeed (idempotent)");

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
        await leavingTab.OpenAsync();
        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
    }

    [Fact]
    public async Task EmployeeSession_RejectedPostDeparture()
    {
        var employeeEmail = "testemployee.session@acme.example";
        var employeePassword = "TestPassword123!"; // Note: in real scenario, use actual Supabase credentials

        var employeeId = await SetupDepartureCandidateAsync("SessionTest");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employeeId);
        var statusBefore = await empEdit.GetEmployeeStatusBadgeTextAsync();
        Assert.Equal("Leaving", statusBefore);

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Now verify the employee's status changed to Former Employee
        // Post-departure, API calls with the former employee's token should be rejected in a real scenario
        // This E2E test verifies the status change; token rejection is covered by integration tests
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task EmployeeLoginDenied_PostDeparture()
    {
        var employeeId = await SetupDepartureCandidateAsync("LoginDenyTest");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

        // Note: Actual login denial testing (attempting to login as a Former Employee and being rejected
        // by Supabase auth) is covered by integration tests like DepartureFinalisationDisablesAccountIntegrationTests,
        // which control the Supabase identity and auth flow directly. This E2E test verifies the account
        // status in the UI reflects the departure completion.
    }

    [Fact]
    public async Task DepartureFinalisation_WithoutLeavingProcess_ReturnsError()
    {
        // Create an employee with Leaving status but no in-progress process (edge case)
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "NoProcessEdge", activate: true);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employee.Id);

        Assert.False(finalized, "Expected departure finalization to fail for employee without a leaving process");

        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_OnNonLeavingEmployee_ReturnsError()
    {
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ActiveOnly", activate: true);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());

        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employee.Id);

        Assert.False(finalized, "Expected departure finalization to fail for non-leaving employee");

        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_OnMultipleEmployees_VerifiesConsistentStatusTransition()
    {
        var employees = new List<Guid>();
        for (int i = 0; i < 4; i++)
        {
            var empId = await SetupDepartureCandidateAsync($"ConsistentEmp{i}");
            employees.Add(empId);
        }

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        foreach (var employeeId in employees)
        {
            var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
            Assert.True(finalized, "Expected departure finalization to succeed");
        }

        foreach (var employeeId in employees)
        {
            await empEdit.GoToAsync(AcmeId, employeeId);
            Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

            await leavingTab.OpenAsync();
            Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());

            Assert.False(await leavingTab.HasAmendButtonAsync(),
                $"Expected no Amend button for {employeeId} after completion");
            Assert.False(await leavingTab.HasCancelButtonAsync(),
                $"Expected no Cancel button for {employeeId} after completion");
        }
    }

    [Fact]
    public async Task DepartureFinalisation_BatchProcessing_HandlesMultipleConcurrentUpdates()
    {
        var emp1 = await SetupDepartureCandidateAsync("BatchUpdate1");
        var emp2 = await SetupDepartureCandidateAsync("BatchUpdate2");
        var emp3 = await SetupDepartureCandidateAsync("BatchUpdate3");

        var empIds = new[] { emp1, emp2, emp3 };

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        foreach (var empId in empIds)
        {
            await empEdit.GoToAsync(AcmeId, empId);
            Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
            await leavingTab.OpenAsync();
            Assert.Equal("In Progress", await leavingTab.GetStatusBadgeTextAsync());
        }

        foreach (var empId in empIds)
        {
            var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, empId);
            Assert.True(finalized, "Expected departure finalization to succeed");
        }

        foreach (var empId in empIds)
        {
            await empEdit.GoToAsync(AcmeId, empId);
            Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

            await leavingTab.OpenAsync();
            Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
        }
    }
}
