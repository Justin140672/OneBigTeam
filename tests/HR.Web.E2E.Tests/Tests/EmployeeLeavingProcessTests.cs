using System.Globalization;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Employee Leaving Process feature (Slice 5): the "Leaving" tab and header "Start
/// Leaving Process" button on the employee edit page, the StartLeavingProcessDialog wizard (five
/// linear steps: resignation date, auto-computed-but-editable leaving date, last working day,
/// leaving reason, confirm), and the resulting read-only Leaving tab.
///
/// Like Offboarding, a leaving process only ever exists once "Start Leaving Process" has been
/// submitted successfully — there is no seed data or auto-provisioning path, and the tab itself
/// is hidden entirely until then (see EmployeeEdit.razor's _showLeavingTab /
/// GetEmployeeResponse.ShowLeavingTab). Unlike Offboarding, the dialog is reached directly from
/// the Employee Overview header (not via the tab's own empty state), and this slice has no
/// cancel/edit action yet — the tab is read-only (Slice 6 will add editing), so there is no
/// delete/deactivate coverage here.
///
/// Every test creates a fresh employee through the standard New Employee form (mirroring
/// EmployeeOffboardingTabTests.cs's CreateEmployeeAsync), which reliably has no leaving process
/// yet and a resolvable effective notice period (falling back to the company default).
/// </summary>
public sealed class EmployeeLeavingProcessTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    // Uses a dedicated pre-seeded pool employee (SeededE2eEmployees.LeavingProcess[slot]) — a
    // "QA Engineer" with no leaving process and a company-default effective notice period, exactly
    // as the old New Employee form flow produced. Tests that actually start/amend/cancel a leaving
    // process take a distinct slot; the read-only / validation-only tests (which never confirm the
    // wizard) share slot 0. Leaves the caller on the employee's edit page.
    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, int slot)
    {
        _ = empList;
        var seeded = SeededE2eEmployees.LeavingProcess[slot];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);
        return seeded.EmployeeId;
    }

    private readonly record struct LeavingWizardResult(string ResignationSummary, string LeavingSummary, string ReasonLabel);

    /// <summary>
    /// Drives the (already-open-via-OpenAsync) Start Leaving Process wizard end to end: fills
    /// the resignation date, reads back the auto-computed leaving date (reusing it verbatim as
    /// the last working day, since it must be on or before the leaving date regardless of this
    /// employee's actual effective notice period length), picks <paramref name="reasonLabel"/>,
    /// asserts the confirmation summary reflects everything entered, then confirms. Returns the
    /// "dd MMM yyyy"-formatted values expected on the resulting read-only Leaving tab, so callers
    /// can assert against them without duplicating date-format conversions.
    /// </summary>
    private async Task<LeavingWizardResult> StartLeavingProcessViaWizardAsync(
        StartLeavingProcessDialog dialog, string resignationDdMMyyyy, string reasonLabel)
    {
        var expectedResignationSummary = DateOnly
            .ParseExact(resignationDdMMyyyy, "dd/MM/yyyy", CultureInfo.InvariantCulture)
            .ToString("dd MMM yyyy");

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(resignationDdMMyyyy);
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw),
            "Expected step 2 to auto-populate a proposed leaving date from the employee's effective notice period");
        var expectedLeavingSummary = DateOnly
            .ParseExact(leavingDateRaw!, "dd/MM/yyyy", CultureInfo.InvariantCulture)
            .ToString("dd MMM yyyy");

        await dialog.ClickNextAsync();

        // Last working day must be on or before the leaving date — reuse the same date so this
        // stays valid regardless of the employee's actual effective notice period length.
        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync(reasonLabel);
        await dialog.ClickNextAsync();

        Assert.Equal(expectedResignationSummary, await dialog.GetConfirmationResignationReceivedDateTextAsync());
        Assert.Equal(expectedLeavingSummary, await dialog.GetConfirmationLeavingDateTextAsync());
        Assert.Equal(expectedLeavingSummary, await dialog.GetConfirmationLastWorkingDayTextAsync());
        Assert.Equal(reasonLabel, await dialog.GetConfirmationLeavingReasonTextAsync());

        await dialog.ConfirmAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        // StartLeavingProcessDialog's OnCompleted callback force-navigates the parent page to
        // "?tab=leaving" — wait for the resulting full page reload to reconnect before the
        // caller reads anything else off the page.
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        return new LeavingWizardResult(expectedResignationSummary, expectedLeavingSummary, reasonLabel);
    }

    [Fact]
    public async Task LeavingTab_IsHidden_AndStartButtonVisible_OnNewlyCreatedEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        Assert.False(await leavingTab.IsTabVisibleAsync(),
            "Expected no 'Leaving' tab for an employee with no leaving process");
        Assert.True(await leavingTab.HasStartLeavingProcessButtonAsync(),
            "Expected a 'Start Leaving Process' button on the Employee Overview header instead");
    }

    [Fact]
    public async Task StartLeavingProcess_FullWizard_LandsOnLeavingTab_ShowsLeavingStatus_AndHidesStartButton()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);
        var leavingTab = new EmployeeLeavingTab(_page);
        var employee = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 1);

        await StartLeavingProcessViaWizardAsync(dialog, "01/09/2026", "Resignation");

        Assert.Equal("Leaving", await employee.GetActiveTabNameAsync());
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
        Assert.False(await leavingTab.HasStartLeavingProcessButtonAsync(),
            "Expected the header 'Start Leaving Process' button to disappear once a leaving process is active");
    }

    [Fact]
    public async Task LeavingTab_PersistsDetails_AfterReload()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, slot: 2);

        var result = await StartLeavingProcessViaWizardAsync(dialog, "15/10/2026", "End of Contract");

        // Revisit the employee's profile fresh (no query string) — the tab should render on its
        // own now that a process is active, showing the same details entered above.
        await empEdit.GoToAsync(AcmeId, employeeId);
        await leavingTab.OpenAsync();

        Assert.Equal(result.ResignationSummary, await leavingTab.GetResignationReceivedDateTextAsync());
        Assert.Equal(result.LeavingSummary, await leavingTab.GetLeavingDateTextAsync());
        Assert.Equal(result.LeavingSummary, await leavingTab.GetLastWorkingDayTextAsync());
        Assert.Equal(result.ReasonLabel, await leavingTab.GetLeavingReasonTextAsync());
        Assert.Equal("In Progress", await leavingTab.GetStatusBadgeTextAsync());

        var noticePeriod = await leavingTab.GetNoticePeriodTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(noticePeriod),
            "Expected a resolved Notice Period value to be displayed");

        var noticeSource = await leavingTab.GetNoticeSourceTextAsync();
        Assert.True(
            noticeSource is "Employee" or "Position Profile" or "Company Default",
            $"Expected a recognised Notice Source label, got '{noticeSource}'");
    }

    [Fact]
    public async Task StartLeavingProcess_WithoutLeavingReason_KeepsWizardOnReasonStepWithValidationError()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync("01/09/2026");
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw));
        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        // Deliberately leave "Leaving Reason" unselected and try to advance to the confirmation step.
        await dialog.ClickNextAsync();

        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to stay open when Leaving Reason is missing");
        Assert.Equal("4. Reason", await dialog.GetActiveStepLabelAsync());

        var error = await dialog.GetStepErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Start Leaving Process dialog");
        Assert.Contains("leaving reason", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartLeavingProcess_WithoutLastWorkingDay_KeepsWizardOnLastWorkingDayStepWithValidationError()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync("01/09/2026");
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw));
        await dialog.ClickNextAsync();

        // Deliberately leave "Last Working Day" unset and try to advance to the Leaving Reason step.
        await dialog.ClickNextAsync();

        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to stay open when Last Working Day is missing");
        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());

        var error = await dialog.GetStepErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Start Leaving Process dialog");
        Assert.Contains("last working day", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Amend / Cancel (Slice 6) ─────────────────────────────────────────────────
    //
    // Every test below first drives a fresh employee through the existing Start Leaving Process
    // wizard (StartLeavingProcessViaWizardAsync) to reach an "InProgress" leaving process, since
    // Amend/Cancel are only reachable from there. Per StartLeavingProcessHandler, starting a
    // leaving process always triggers IOffboardingPlanCoordinator.StartAsync as a side effect, so
    // by the time these tests reach Amend/Cancel, offboarding has already started for the
    // employee — meaning the "offboarding already started" warning paths in both dialogs are the
    // realistic default outcome here, not an edge case requiring extra setup.

    [Fact]
    public async Task AmendLeavingProcess_PrePopulatesCurrentValues_AppliesChanges_AndShowsOffboardingWarning_AfterReload()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var amendDialog = new AmendLeavingProcessDialog(_page);
        var leavingTab  = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 3);

        var started = await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        Assert.True(await leavingTab.HasAmendButtonAsync(),
            "Expected an 'Amend' button while the leaving process is InProgress");
        Assert.True(await leavingTab.HasCancelButtonAsync(),
            "Expected a 'Cancel Leaving Process' button while the leaving process is InProgress");

        // StartLeavingProcessViaWizardAsync reused the auto-computed leaving date verbatim as
        // the last working day, so both should be pre-populated with that same date here.
        var expectedCurrentDdMMyyyy = DateOnly
            .ParseExact(started.LeavingSummary, "dd MMM yyyy", CultureInfo.InvariantCulture)
            .ToString("dd/MM/yyyy");

        await amendDialog.OpenAsync();

        Assert.Equal(expectedCurrentDdMMyyyy, await amendDialog.GetLeavingDateTextAsync());
        Assert.Equal(expectedCurrentDdMMyyyy, await amendDialog.GetLastWorkingDayTextAsync());
        Assert.Equal(started.ReasonLabel, await amendDialog.GetLeavingReasonTextAsync());

        const string newDateDdMMyyyy = "20/10/2026";
        var expectedNewSummary = DateOnly
            .ParseExact(newDateDdMMyyyy, "dd/MM/yyyy", CultureInfo.InvariantCulture)
            .ToString("dd MMM yyyy");

        await amendDialog.FillLeavingDateAsync(newDateDdMMyyyy);
        await amendDialog.FillLastWorkingDayAsync(newDateDdMMyyyy);
        await amendDialog.SelectLeavingReasonAsync("Redundancy");

        await amendDialog.SaveAsync();
        Assert.False(await amendDialog.IsVisibleAsync(),
            "Expected the Amend Leaving Process dialog to close after a successful save");

        // OnLeavingProcessAmended force-navigates the parent page to "?tab=leaving" (plus
        // "&offboardingAlreadyStarted=true" here) — wait for the resulting full page reload.
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        Assert.Equal(expectedNewSummary, await leavingTab.GetLeavingDateTextAsync());
        Assert.Equal(expectedNewSummary, await leavingTab.GetLastWorkingDayTextAsync());
        Assert.Equal("Redundancy", await leavingTab.GetLeavingReasonTextAsync());
        Assert.Equal("In Progress", await leavingTab.GetStatusBadgeTextAsync());

        Assert.True(await leavingTab.HasOffboardingAlreadyStartedWarningAsync(),
            "Expected the 'Offboarding has already started' banner after amending, since Start " +
            "Leaving Process always triggers offboarding to start as a side effect");
    }

    [Fact]
    public async Task AmendLeavingProcess_WithLastWorkingDayAfterLeavingDate_ShowsValidationError_AndKeepsDialogOpen()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var amendDialog = new AmendLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 4);

        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        await amendDialog.OpenAsync();

        // Deliberately set Last Working Day after Leaving Date.
        await amendDialog.FillLeavingDateAsync("10/10/2026");
        await amendDialog.FillLastWorkingDayAsync("15/10/2026");

        await amendDialog.SaveAsync();

        Assert.True(await amendDialog.IsVisibleAsync(),
            "Expected the Amend Leaving Process dialog to stay open when Last Working Day is after Leaving Date");

        var error = await amendDialog.GetErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Amend Leaving Process dialog");
        Assert.Contains("last working day", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelLeavingProcess_WithOffboardingTasksWarning_CancelsProcess_AndReturnsEmployeeToActive()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog  = new StartLeavingProcessDialog(_page);
        var cancelDialog = new CancelLeavingProcessDialog(_page);
        var leavingTab   = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 5);

        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        await cancelDialog.OpenAsync();

        // Offboarding always auto-starts as a side effect of Start Leaving Process (see comment
        // above), so the stronger "outstanding offboarding tasks" warning is the realistic
        // default outcome here, not an edge case.
        Assert.True(await cancelDialog.HasOffboardingTasksWarningAsync(),
            "Expected the stronger 'offboarding tasks will also be cancelled' warning, since " +
            "Start Leaving Process always triggers offboarding to start as a side effect");

        await cancelDialog.FillCancellationReasonAsync("Employee withdrew resignation.");
        await cancelDialog.ConfirmAsync();

        Assert.False(await cancelDialog.IsVisibleAsync(),
            "Expected the Cancel Leaving Process dialog to close after a successful cancellation");

        // OnLeavingProcessCancelled force-navigates to the plain employee URL (no ?tab=, since
        // the Leaving tab disappears once the process is Cancelled) — wait for the reload.
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        Assert.False(await leavingTab.IsTabVisibleAsync(),
            "Expected the 'Leaving' tab to disappear once the leaving process is Cancelled");
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());
        Assert.True(await leavingTab.HasStartLeavingProcessButtonAsync(),
            "Expected the header 'Start Leaving Process' button to reappear once the employee is Active again");
    }

    [Fact]
    public async Task CancelLeavingProcess_WithEmptyReason_ShowsValidationError_AndKeepsDialogOpen()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog  = new StartLeavingProcessDialog(_page);
        var cancelDialog = new CancelLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 6);

        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        await cancelDialog.OpenAsync();

        // Deliberately leave the Cancellation Reason blank and try to submit.
        await cancelDialog.ConfirmAsync();

        Assert.True(await cancelDialog.IsVisibleAsync(),
            "Expected the Cancel Leaving Process dialog to stay open when Cancellation Reason is blank");

        var error = await cancelDialog.GetErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Cancel Leaving Process dialog");
        Assert.Contains("reason", error, StringComparison.OrdinalIgnoreCase);
    }
}
