
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeOffboardingTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, int slot)
    {
        _ = empList;
        var seeded = SeededE2eEmployees.OffboardingTab[slot];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);
        return seeded.EmployeeId;
    }

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

        await CreateEmployeeAsync(empList, empEdit, slot: 0);

        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Offboarding"),
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

        var employeeId = await CreateEmployeeAsync(empList, empEdit, slot: 1);

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

        await empEdit.GoToAsync(AcmeId, employeeId, "tab=offboarding");
        Assert.Equal("Leaving & Offboarding", await employee.GetActiveTabNameAsync());
    }

    [Fact]
    public async Task WaiveObligation_WithRequiredReason_ShowsWaivedStatus_AndReasonVisible()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var offboarding = new EmployeeOffboardingTab(_page);
        var waiveDialog = new WaiveOffboardingTaskDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, slot: 3);

        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        await offboarding.OpenAsync();

        const string task = "Review outstanding documents for employee exit";

        Assert.True(await offboarding.HasWaiveButtonAsync(task),
            "Expected a 'Waive' action on the mandatory, still-outstanding checklist obligation");

        await offboarding.ClickWaiveAsync(task);
        Assert.True(await waiveDialog.IsVisibleAsync());

        // Deliberately submit without a reason first — Reason is required.
        await waiveDialog.ConfirmAsync();
        Assert.True(await waiveDialog.IsVisibleAsync(),
            "Expected the Waive Obligation dialog to stay open when Reason is blank");
        var missingReasonError = await waiveDialog.GetErrorAsync();
        Assert.False(string.IsNullOrWhiteSpace(missingReasonError),
            "Expected an inline validation error requiring a waiver reason");
        Assert.Contains("reason", missingReasonError, StringComparison.OrdinalIgnoreCase);

        const string reason = "Employee has no company documents outstanding — confirmed with HR admin.";
        await waiveDialog.FillReasonAsync(reason);
        await waiveDialog.ConfirmAsync();
        Assert.False(await waiveDialog.IsVisibleAsync(),
            "Expected the Waive Obligation dialog to close after a successful waive");

        var status = await offboarding.GetChecklistTaskStatusAsync(task);
        Assert.Equal("Waived", status);

        var waiveReasonText = await offboarding.GetChecklistTaskWaiveReasonAsync(task);
        Assert.False(string.IsNullOrWhiteSpace(waiveReasonText));
        Assert.Contains(reason, waiveReasonText);

        Assert.False(await offboarding.HasWaiveButtonAsync(task),
            "Expected no further 'Waive' action once the obligation is already Waived");
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

        await CreateEmployeeAsync(empList, empEdit, slot: 2);

        await StartLeavingProcessViaWizardAsync(startDialog, "15/09/2026", "Resignation");

        await offboarding.OpenAsync();

        Assert.True(await offboarding.HasChecklistCardAsync(),
            "Expected the Offboarding Checklist card to be visible after the leaving process triggered a plan");

        Assert.True(
            await offboarding.HasChecklistTaskAsync("Review outstanding documents for employee exit"),
            "Expected the fixed HR document-review task to appear in the checklist");

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
            await offboarding.HasChecklistTaskAsync("Notify IT and Finance of employee exit"),
            "Expected the fixed IT/Finance notification task to appear in the checklist");
    }
}
