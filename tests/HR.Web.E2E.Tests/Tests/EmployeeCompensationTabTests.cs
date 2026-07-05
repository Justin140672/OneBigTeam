using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Compensation tab on the employee edit page.
///
/// Uses the seeded "Sarah Chen" employee (ID: 30000000-0000-0000-0000-000000000001) who has two
/// seeded Annual compensation records: a closed starting salary of 120,000 GBP (6 Jan 2020 to
/// 31 Dec 2022) and the current, open-ended 145,000 GBP record effective 1 Jan 2023.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeCompensationTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId     = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahChen  = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompensationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Compensation" }).IsVisibleAsync(),
            "Expected a 'Compensation' tab on the employee edit page");
    }

    [Fact]
    public async Task CompensationTab_ShowsCurrentCompensationPanel_WithExpectedFields()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenCompensationTabAsync();

        Assert.True(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected the Current Compensation panel to be visible");

        var salary = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains("145,000.00", salary);
        Assert.Contains("per year", salary);

        var annualisedSalary = await empEdit.GetCompensationFieldTextAsync("compensation-annualised-salary");
        Assert.Contains("145,000.00", annualisedSalary);

        var hours = await empEdit.GetCompensationFieldTextAsync("compensation-hours");
        Assert.Contains("37.5", hours);

        var fte = await empEdit.GetCompensationFieldTextAsync("compensation-fte");
        Assert.Contains("100", fte);

        var effectiveFrom = await empEdit.GetCompensationFieldTextAsync("compensation-effective-from");
        Assert.Contains("2023", effectiveFrom);
    }

    [Fact]
    public async Task CompensationTab_ShowsHistoryGrid_WithBothRecords()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenCompensationTabAsync();

        var grid = _page.Locator("[data-testid='compensation-history-grid']");
        Assert.True(await grid.IsVisibleAsync(), "Expected the Compensation History grid to be visible");

        var gridText = await grid.TextContentAsync();
        Assert.Contains("145,000.00", gridText);
        Assert.Contains("120,000.00", gridText);
        Assert.Contains("Annual", gridText);
    }

    [Fact]
    public async Task CompensationTab_ShowsEmptyState_ForEmployeeWithNoCompensationRecord()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Tom Williams has no seeded compensation record.
        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenCompensationTabAsync();

        Assert.False(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected no Current Compensation panel for an employee without a compensation record");

        Assert.True(await _page.Locator(".alert-secondary").IsVisibleAsync(),
            "Expected an empty-state message when no compensation record exists");
    }

    [Fact]
    public async Task AddCompensation_WithFutureEffectiveDate_AppearsInHistoryWithEditAndDeleteActions()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Tom Williams has no existing records, so a future-dated add here can't collide with
        // any auto-close/overlap logic exercised by other tests.
        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenCompensationTabAsync();

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/01/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("38000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        var row = empEdit.CompensationHistoryRow("1 Jan 2030");
        Assert.True(await row.First.IsVisibleAsync(),
            "Expected the newly added future-dated record to appear in the history grid");

        // Future-dated rows show Edit/Delete; past/current ones don't.
        Assert.True(await row.GetByTitle("Edit").IsVisibleAsync(), "Expected an Edit action on the future-dated row");
        Assert.True(await row.GetByTitle("Delete").IsVisibleAsync(), "Expected a Delete action on the future-dated row");
    }

    [Fact]
    public async Task EditFutureCompensation_UpdatesSalary_WithoutChangingEffectiveDate()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenCompensationTabAsync();

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/06/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("40000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        await empEdit.ClickEditCompensationRowAsync("1 Jun 2030");
        await empEdit.FillEditCompensationSalaryAsync("42000");
        await empEdit.SubmitEditCompensationDialogAsync();

        var row = empEdit.CompensationHistoryRow("1 Jun 2030");
        var rowText = await row.First.TextContentAsync();
        Assert.Contains("42,000.00", rowText);
    }

    [Fact]
    public async Task DeleteFutureCompensation_RemovesRecordFromHistory()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenCompensationTabAsync();

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/12/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("41000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        await empEdit.ClickDeleteCompensationRowAsync("1 Dec 2030");
        await empEdit.ConfirmDeleteCompensationAsync();

        Assert.False(await empEdit.CompensationHistoryRow("1 Dec 2030").First.IsVisibleAsync(),
            "Expected the deleted future-dated record to no longer appear in the history grid");
    }
}
