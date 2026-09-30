using System.Globalization;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeLeavingProcessTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private static string DaysFromToday(int days) =>
        DateTime.Today.AddDays(days).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string ResignationReceivedToday => DaysFromToday(0);

    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, int slot)
    {
        _ = empList;
        var seeded = SeededE2eEmployees.LeavingProcess[slot];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);
        return seeded.EmployeeId;
    }

    private readonly record struct LeavingWizardResult(string ResignationSummary, string LeavingSummary, string ReasonLabel);

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

        await StartLeavingProcessViaWizardAsync(dialog, ResignationReceivedToday, "Resignation");

        Assert.Equal("Leaving & Offboarding", await employee.GetActiveTabNameAsync());
        Assert.Equal("Leaving", await empEdit.GetEmployeeStatusBadgeTextAsync());
        Assert.False(await leavingTab.HasStartLeavingProcessButtonAsync(),
            "Expected the header 'Start Leaving Process' button to disappear once a leaving process is active");

        Assert.True(await leavingTab.Checklist.HasChecklistCardAsync(),
            "Expected the offboarding checklist to appear automatically once the leaving process was confirmed");
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

        var result = await StartLeavingProcessViaWizardAsync(dialog, DaysFromToday(14), "End of Contract");

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
        await dialog.FillResignationReceivedDateAsync(ResignationReceivedToday);
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
        Assert.Equal("4. Reason & Notes", await dialog.GetActiveStepLabelAsync());

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
        await dialog.FillResignationReceivedDateAsync(ResignationReceivedToday);
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

    [Fact]
    public async Task StartLeavingProcess_WithoutResignationReceivedDate_KeepsWizardOnStep1WithValidationError()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        await dialog.OpenAsync();

        // Deliberately click Next on step 1 without picking a resignation received date.
        await dialog.ClickNextAsync();

        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to stay open when the resignation received date is missing");
        Assert.Equal("1. Resignation Date", await dialog.GetActiveStepLabelAsync());

        var error = await dialog.GetStepErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Start Leaving Process dialog");
        Assert.Contains("resignation", error, StringComparison.OrdinalIgnoreCase);

        await dialog.FillResignationReceivedDateAsync(ResignationReceivedToday);
        await dialog.ClickNextAsync();
        Assert.Equal("2. Leaving Date", await dialog.GetActiveStepLabelAsync());
    }

    [Fact]
    public async Task StartLeavingProcess_WithoutLeavingDate_KeepsWizardOnStep2WithValidationError()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(ResignationReceivedToday);
        await dialog.ClickNextAsync();
        Assert.Equal("2. Leaving Date", await dialog.GetActiveStepLabelAsync());

        await dialog.ClearLeavingDateAsync();
        await dialog.ClickNextAsync();

        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to stay open when the leaving date is missing");
        Assert.Equal("2. Leaving Date", await dialog.GetActiveStepLabelAsync());

        var error = await dialog.GetStepErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error inside the Start Leaving Process dialog");
        Assert.Contains("leaving date", error, StringComparison.OrdinalIgnoreCase);

        await dialog.FillLeavingDateAsync(DaysFromToday(30));
        await dialog.ClickNextAsync();
        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());
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

        var started = await StartLeavingProcessViaWizardAsync(startDialog, ResignationReceivedToday, "Resignation");

        Assert.True(await leavingTab.WaitForAmendButtonAsync(),
            "Expected an 'Amend' button while the leaving process is InProgress");
        Assert.True(await leavingTab.HasCancelButtonAsync(),
            "Expected a 'Cancel Leaving Process' button while the leaving process is InProgress");

        var expectedCurrentDdMMyyyy = DateOnly
            .ParseExact(started.LeavingSummary, "dd MMM yyyy", CultureInfo.InvariantCulture)
            .ToString("dd/MM/yyyy");

        await amendDialog.OpenAsync();

        Assert.Equal(expectedCurrentDdMMyyyy, await amendDialog.GetLeavingDateTextAsync());
        Assert.Equal(expectedCurrentDdMMyyyy, await amendDialog.GetLastWorkingDayTextAsync());
        Assert.Equal(started.ReasonLabel, await amendDialog.GetLeavingReasonTextAsync());

        var newDateDdMMyyyy = DaysFromToday(60);
        var expectedNewSummary = DateOnly
            .ParseExact(newDateDdMMyyyy, "dd/MM/yyyy", CultureInfo.InvariantCulture)
            .ToString("dd MMM yyyy");

        await amendDialog.FillLeavingDateAsync(newDateDdMMyyyy);
        await amendDialog.FillLastWorkingDayAsync(newDateDdMMyyyy);
        await amendDialog.SelectLeavingReasonAsync("Redundancy");

        await amendDialog.SaveAsync();
        Assert.False(await amendDialog.IsVisibleAsync(),
            "Expected the Amend Leaving Process dialog to close after a successful save");

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

        await StartLeavingProcessViaWizardAsync(startDialog, ResignationReceivedToday, "Resignation");

        await amendDialog.OpenAsync();

        // Deliberately set Last Working Day after Leaving Date.
        await amendDialog.FillLeavingDateAsync(DaysFromToday(40));
        await amendDialog.FillLastWorkingDayAsync(DaysFromToday(45));

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

        await StartLeavingProcessViaWizardAsync(startDialog, ResignationReceivedToday, "Resignation");

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

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        Assert.False(await leavingTab.IsTabVisibleAsync(),
            "Expected the 'Leaving' tab to disappear once the leaving process is Cancelled");
        Assert.Equal("Active", await empEdit.GetEmployeeStatusBadgeTextAsync());
        Assert.True(await leavingTab.HasStartLeavingProcessButtonAsync(),
            "Expected the header 'Start Leaving Process' button to reappear once the employee is Active again");
    }

    [Fact]
    public async Task CancelLeavingProcess_ShowsCancelledStatus_NoAmendOrCancelActions_AndReadOnlyChecklist()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog  = new StartLeavingProcessDialog(_page);
        var cancelDialog = new CancelLeavingProcessDialog(_page);
        var leavingTab   = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, slot: 7);

        await StartLeavingProcessViaWizardAsync(startDialog, ResignationReceivedToday, "Resignation");

        await cancelDialog.OpenAsync();
        await cancelDialog.FillCancellationReasonAsync("Employee withdrew resignation.");
        await cancelDialog.ConfirmAsync();
        Assert.False(await cancelDialog.IsVisibleAsync());

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=leaving");
        await leavingTab.OpenAsync();

        Assert.Equal("Cancelled", await leavingTab.GetStatusBadgeTextAsync());
        Assert.False(await leavingTab.HasAmendButtonAsync(),
            "Expected no 'Amend' action once the leaving process is Cancelled");
        Assert.False(await leavingTab.HasCancelButtonAsync(),
            "Expected no 'Cancel Leaving Process' action once the leaving process is already Cancelled");

        Assert.False(
            await leavingTab.Checklist.HasWaiveButtonAsync("Review outstanding documents for employee exit"),
            "Expected the checklist to be read-only (no Waive action) once the leaving process is Cancelled");
    }

    [Fact]
    public async Task StartLeavingProcess_WithReasonOther_RequiresNotes_ThenAllowsSubmission()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);
        var leavingTab = new EmployeeLeavingTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var seeded = SeededE2eEmployees.OffboardingConfirmation[1];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(ResignationReceivedToday);
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw));
        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync("Other");

        // Deliberately leave Notes blank and try to advance — Notes is required when the reason is "Other".
        await dialog.ClickNextAsync();

        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the wizard to stay open when Notes is blank and Leaving Reason is 'Other'");
        Assert.Equal("4. Reason & Notes", await dialog.GetActiveStepLabelAsync());

        var error = await dialog.GetStepErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error),
            "Expected an inline validation error requiring Notes when the reason is 'Other'");
        Assert.Contains("notes", error, StringComparison.OrdinalIgnoreCase);

        await dialog.FillNotesAsync("Employee is relocating overseas for personal reasons.");
        await dialog.ClickNextAsync();
        Assert.Equal("5. Confirm", await dialog.GetActiveStepLabelAsync());

        await dialog.ConfirmAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission with Notes filled");

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
        await leavingTab.OpenAsync();

        Assert.Equal("Other", await leavingTab.GetLeavingReasonTextAsync());
        Assert.Equal("Employee is relocating overseas for personal reasons.", await leavingTab.GetNotesTextAsync());
    }

    [Fact]
    public async Task UnifiedWorkspace_IsReachable_ViaBothLeavingAndLegacyOffboardingTabQueryValues()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var leavingTab  = new EmployeeLeavingTab(_page);
        var employee    = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var seeded = SeededE2eEmployees.OffboardingConfirmation[2];
        var employeeId = seeded.EmployeeId;
        await empEdit.GoToAsync(AcmeId, employeeId);

        await StartLeavingProcessViaWizardAsync(startDialog, ResignationReceivedToday, "Resignation");

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=leaving");
        await leavingTab.OpenAsync();
        Assert.Equal("Leaving & Offboarding", await employee.GetActiveTabNameAsync());
        var detailsViaLeaving = await leavingTab.GetLeavingDateTextAsync();
        Assert.True(await leavingTab.Checklist.HasChecklistCardAsync());

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=offboarding");
        await leavingTab.OpenAsync();
        Assert.Equal("Leaving & Offboarding", await employee.GetActiveTabNameAsync());
        Assert.Equal(detailsViaLeaving, await leavingTab.GetLeavingDateTextAsync());
        Assert.True(await leavingTab.Checklist.HasChecklistCardAsync());
    }

    /// <summary>
    /// Definition-of-done item 1 (empty-state half): an employee with no leaving process at all has
    /// the "Leaving &amp; Offboarding" section hidden from the strip entirely, and neither the
    /// current nor legacy "?tab=" query value force-shows it — EmployeeEdit.razor's deep-link
    /// fallback (SectionVisible check in LoadAsync) instead lands on Details for both, matching the
    /// pre-merge behaviour. This is the closest reachable proxy for the "no leaving process" empty
    /// state without a manufactured server-side edge case — see this file's remarks and the task
    /// report for why the literal "No leaving process has been started" HrEmptyState text (only
    /// rendered once the section is already visible but the lookup still 404s) isn't reachable via
    /// any real UI flow for a brand new employee.
    /// </summary>
    [Fact]
    public async Task UnifiedWorkspace_IsNotShown_ViaEitherTabQueryValue_ForEmployeeWithNoLeavingProcess()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var employee = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, slot: 0);

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=leaving");
        Assert.Equal("Details", await employee.GetActiveTabNameAsync());

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=offboarding");
        Assert.Equal("Details", await employee.GetActiveTabNameAsync());
    }

    [Fact]
    public async Task StartLeavingProcess_WithBackdatedLeavingDate_RequiresConfirmationCheckbox()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog  = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var seeded = SeededE2eEmployees.OffboardingConfirmation[3];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);

        var receivedToday = DateOnly.FromDateTime(DateTime.Today).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(receivedToday);
        await dialog.ClickNextAsync();

        var autoDate = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(autoDate));
        Assert.False(await dialog.IsBackdatedConfirmationVisibleAsync(),
            "Expected no backdating checkbox for a future-dated leaving date");

        await dialog.FillLeavingDateAsync("01/01/2024");
        Assert.True(await dialog.IsBackdatedConfirmationVisibleAsync(),
            "Expected the backdating checkbox to appear once the leaving date is in the past");

        await dialog.ClickBackAsync();
        await dialog.FillResignationReceivedDateAsync("01/12/2023");
        await dialog.ClickNextAsync();
        Assert.Equal("01/01/2024", await dialog.GetLeavingDateTextAsync());

        await dialog.ClickNextAsync();
        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the wizard to stay on step 2 until the backdating checkbox is confirmed");
        var error = await dialog.GetStepErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("backdate", error, StringComparison.OrdinalIgnoreCase);

        await dialog.CheckBackdatedConfirmationAsync();
        await dialog.ClickNextAsync();
        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());
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

        await StartLeavingProcessViaWizardAsync(startDialog, ResignationReceivedToday, "Resignation");

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
