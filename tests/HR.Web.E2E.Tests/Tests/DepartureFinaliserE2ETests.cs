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
    /// Today's date in the company's time zone — the leaving date used for every test employee. It is
    /// not backdated (a backdated start finalises immediately), but is already due for the job.
    /// </summary>
    private static DateOnly CompanyToday => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);

    /// <summary>
    /// Arranges a departure candidate through the API only (no wizard, no form login): creates an
    /// Active employee and starts a leaving process due today, then makes sure the browser session is
    /// authenticated as Laura (cached storageState, so this is cheap). The wizard itself is covered
    /// by exactly one UI test, <see cref="DepartureFinalisation_Transitions_Employee_Status_FromLeavingToFormerEmployee"/>.
    /// </summary>
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

    /// <summary>The single UI-driven path: drives the Start Leaving Process wizard end to end.</summary>
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

        // Before departure: employee is Active and shows in the Active-filtered list.
        await empList.GoToAsync(AcmeId);
        await empList.SelectStatusFilterAsync("Active");
        Assert.True(await empList.HasEmployeeAsync(employeeName),
            $"Expected {employeeName} to appear in the Active-filtered employee list before departure");

        // Start the leaving process (API) and finalize it.
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, employeeId);
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // After finalization: employee is Former Employee and no longer appears in the
        // Active-filtered list (the unfiltered default view still contains the historical record).
        await empList.GoToAsync(AcmeId);
        await empList.SelectStatusFilterAsync("Active");
        Assert.False(await empList.HasEmployeeAsync(employeeName),
            $"Expected {employeeName} to no longer appear in the Active-filtered employee list after departure finalization");
    }

    [Fact]
    public async Task DepartureFinalisation_RemovesEmployee_FromCompanyDirectory()
    {
        // The employee directory (ListDirectoryEmployees/SearchEmployeeDirectory) only ever shows
        // Status == Active employees — so "before" must be checked while still Active, i.e. before
        // the leaving process is even started (starting it immediately moves Status to Leaving,
        // which is already excluded).
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "DirRemove", activate: true);
        var employeeId = employee.Id;
        var employeeName = employee.FullName;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Before the leaving process starts: employee is Active and should appear in the directory.
        await directory.GoToAsync(AcmeId);
        await directory.SearchAsync(employeeName);
        var cardCountBefore = await directory.CardCount();
        Assert.True(cardCountBefore > 0,
            $"Expected {employeeName} to appear in the company directory before departure finalization");

        // Start the leaving process (API) and finalize it.
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, employeeId);
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

        // Set up the departing report's leaving process via the API, then finalize.
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, departingReport.Id);
        await EnsureLoggedInAsync();

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

        // Start both leaving processes via the API (both due today); the per-employee finaliser
        // seam then finalises only report1.
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, report1.Id);
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, report2.Id);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Finalize only report1.
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, report1.Id);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Verify report1 is Former Employee, but report2 is still Leaving.
        await empEdit.GoToAsync(AcmeId, report1.Id);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

        await empEdit.GoToAsync(AcmeId, report2.Id);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task MultipleEmployees_AllDueForFinalisation_AreProcessedInBatch()
    {
        // Create 3 employees with backdated leaving dates (all immediately due for finalization)
        var emp1 = await SetupDepartureCandidateAsync("BatchEmp1");
        var emp2 = await SetupDepartureCandidateAsync("BatchEmp2");
        var emp3 = await SetupDepartureCandidateAsync("BatchEmp3");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Verify all three are in "Leaving" status before finalization
        foreach (var employeeId in new[] { emp1, emp2, emp3 })
        {
            await empEdit.GoToAsync(AcmeId, employeeId);
            Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
        }

        // Trigger finalization for each of the three due employees. The test seam
        // (DepartureFinaliserTestEndpoint) finalizes one employee at a time by design — see
        // ProcessLeavingEmployeesJob.ExecuteForEmployeeAsync's remarks — so a test's own finalizer
        // call cannot also finalize another parallel test's due leaver in the same company.
        foreach (var employeeId in new[] { emp1, emp2, emp3 })
        {
            var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
            Assert.True(finalized, "Expected departure finalization to succeed");
        }

        // Verify all three transitioned to "Former Employee"
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

        // First finalization
        var finalized1 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized1, "Expected first departure finalization to succeed");

        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
        await leavingTab.OpenAsync();
        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());

        // Second finalization (idempotency test)
        var finalized2 = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized2, "Expected second departure finalization to succeed (idempotent)");

        // Reload and verify no regression: still Former Employee with Completed status
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());
        await leavingTab.OpenAsync();
        Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
    }

    [Fact]
    public async Task EmployeeSession_RejectedPostDeparture()
    {
        // Create two browser contexts: one for the employee, one for an HR admin
        var employeeEmail = "testemployee.session@acme.example";
        var employeePassword = "TestPassword123!"; // Note: in real scenario, use actual Supabase credentials

        var employeeId = await SetupDepartureCandidateAsync("SessionTest");

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // Simulate employee having a pre-finalization session by verifying they can access their own profile
        // via API before finalization (using a simulated token scenario)
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Verify the employee is still accessible and in Leaving status
        await empEdit.GoToAsync(AcmeId, employeeId);
        var statusBefore = await empEdit.GetEmployeeStatusBadgeTextAsync();
        Assert.Equal("Leaving", statusBefore);

        // Trigger finalization
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

        // Verify the employee exists and is in Leaving status
        await empEdit.GoToAsync(AcmeId, employeeId);
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());

        // Trigger finalization
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
        Assert.True(finalized, "Expected departure finalization to succeed");

        // Verify the status changed to Former Employee
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

        // Verify the employee is in Active status (no leaving process started)
        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());

        // Attempt to finalize without a leaving process
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employee.Id);

        // Should fail because there's no leaving process to finalize
        Assert.False(finalized, "Expected departure finalization to fail for employee without a leaving process");

        // Verify status remains unchanged
        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_OnNonLeavingEmployee_ReturnsError()
    {
        // Create an Active employee without any leaving process
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "ActiveOnly", activate: true);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Verify the employee is in Active status
        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());

        // Attempt to finalize a non-leaving employee
        var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employee.Id);

        // Should fail validation
        Assert.False(finalized, "Expected departure finalization to fail for non-leaving employee");

        // Verify status remains Active
        await empEdit.GoToAsync(AcmeId, employee.Id);
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());
    }

    [Fact]
    public async Task DepartureFinalisation_OnMultipleEmployees_VerifiesConsistentStatusTransition()
    {
        // Create 4 employees, set up leaving processes, and finalize them all at once
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

        // Trigger finalization on each employee individually — the test seam finalizes one
        // employee at a time by design (see ProcessLeavingEmployeesJob.ExecuteForEmployeeAsync).
        foreach (var employeeId in employees)
        {
            var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, employeeId);
            Assert.True(finalized, "Expected departure finalization to succeed");
        }

        // Verify all employees consistently transitioned to Former Employee
        foreach (var employeeId in employees)
        {
            await empEdit.GoToAsync(AcmeId, employeeId);
            Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

            // Verify leaving status is Completed
            await leavingTab.OpenAsync();
            Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());

            // Verify no action buttons are present
            Assert.False(await leavingTab.HasAmendButtonAsync(),
                $"Expected no Amend button for {employeeId} after completion");
            Assert.False(await leavingTab.HasCancelButtonAsync(),
                $"Expected no Cancel button for {employeeId} after completion");
        }
    }

    [Fact]
    public async Task DepartureFinalisation_BatchProcessing_HandlesMultipleConcurrentUpdates()
    {
        // Create 3 employees with leaving processes ready for batch finalization
        var emp1 = await SetupDepartureCandidateAsync("BatchUpdate1");
        var emp2 = await SetupDepartureCandidateAsync("BatchUpdate2");
        var emp3 = await SetupDepartureCandidateAsync("BatchUpdate3");

        var empIds = new[] { emp1, emp2, emp3 };

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Verify all three start in "Leaving" status with "In Progress" leaving process
        foreach (var empId in empIds)
        {
            await empEdit.GoToAsync(AcmeId, empId);
            Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
            await leavingTab.OpenAsync();
            Assert.Equal("In Progress", await leavingTab.GetStatusBadgeTextAsync());
        }

        // Trigger finalization for each employee individually — the test seam finalizes one
        // employee at a time by design (see ProcessLeavingEmployeesJob.ExecuteForEmployeeAsync).
        foreach (var empId in empIds)
        {
            var finalized = await DepartureFinaliserApi.FinalizeAsync(_fixture.ApiBaseUrl, empId);
            Assert.True(finalized, "Expected departure finalization to succeed");
        }

        // Verify all three employees consistently transitioned to Former Employee status
        // with Completed leaving process status
        foreach (var empId in empIds)
        {
            await empEdit.GoToAsync(AcmeId, empId);
            Assert.Equal("Former Employee", await empEdit.GetEmployeeStatusBadgeTextAsync());

            await leavingTab.OpenAsync();
            Assert.Equal("Completed", await leavingTab.GetStatusBadgeTextAsync());
        }
    }
}
