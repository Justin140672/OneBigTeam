using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Compensation tab on the employee edit page.
///
/// Uses the seeded "Sarah Chen" employee (ID: 30000000-0000-0000-0000-000000000001) who has two
/// seeded Annual compensation records: a closed starting salary of 120,000 GBP (6 Jan 2020 to
/// 31 Dec 2022) and the current, open-ended 145,000 GBP record effective 1 Jan 2023. Sarah is
/// only ever READ here, never mutated.
///
/// The future-dated add/edit/delete tests below each create their own fresh, uniquely-named
/// employee instead of reusing the shared Tom Williams — Tom is mutated by ~40+ other test files
/// running in parallel, so adding/editing/deleting his compensation rows here would race those.
/// </summary>
public sealed class EmployeeCompensationTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId     = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahChen  = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompensationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        // GoToAsync only waits for a combobox to render, not for the full tab list — which
        // depends on the employee's own async-loaded data (_showProbationTab etc.) — so a bare
        // instant IsVisibleAsync() here can race that and report "not visible" for a tab that's
        // genuinely there a moment later. A bounded wait avoids that.
        await _page.GetByRole(AriaRole.Tab, new() { Name = "Compensation History" }).WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Renamed from "Compensation" to "Compensation History" (the separate "Current
        // Compensation" card was removed entirely — see the next test).
        Assert.True(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Compensation History" }).IsVisibleAsync(),
            "Expected a 'Compensation History' tab on the employee edit page");
        Assert.False(
            await _page.GetByRole(AriaRole.Tab, new() { Name = "Compensation", Exact = true }).IsVisibleAsync(),
            "Did not expect a tab labelled exactly 'Compensation' (renamed to 'Compensation History')");
    }

    [Fact]
    public async Task CompensationTab_NoLongerShowsCurrentCompensationCard_ButShowsHistoryGrid()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenCompensationTabAsync();

        Assert.False(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected the 'Current Compensation' panel to have been removed entirely");
        Assert.False(await _page.GetByText("Current Compensation", new() { Exact = true }).IsVisibleAsync(),
            "Did not expect a 'Current Compensation' heading anywhere on the tab");

        // The Compensation History card/grid takes over showing the current record as just
        // another (undated-end) row alongside past records. Scoped to the <h5> card heading
        // specifically — the Compensation History *tab* label is also "Compensation History"
        // exactly, so a bare GetByText match is ambiguous between the two (Playwright strict mode).
        Assert.True(await _page.GetByRole(AriaRole.Heading, new() { Name = "Compensation History", Exact = true }).IsVisibleAsync(),
            "Expected the 'Compensation History' card heading");

        var grid = _page.Locator("[data-testid='compensation-history-grid']");
        Assert.True(await grid.IsVisibleAsync(), "Expected the Compensation History grid to be visible");

        var gridText = await grid.TextContentAsync();
        Assert.Contains("145,000.00", gridText);
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
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Every seeded Acme employee (including Tom Williams — see EmployeesModule's
        // newHireCompensation seed array) already has at least one compensation record, so a
        // genuinely empty compensation history can only be exercised via a freshly created
        // employee.
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoCompensation{unique}";
        var workEmail = $"e2e.nocomp{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        await empEdit.OpenCompensationTabAsync();

        Assert.False(await empEdit.HasCurrentCompensationPanelAsync(),
            "Expected no Current Compensation panel for an employee without a compensation record");

        // With no current record and no history, the tab shows a single unified empty-state
        // message rather than separate "no current" and "no history" messages side by side.
        Assert.True(await _page.Locator("[data-testid='no-compensation-message']").IsVisibleAsync(),
            "Expected a single unified empty-state message when there is no compensation data at all");
    }

    /// <summary>
    /// Creates a fresh, uniquely-named Acme employee and navigates to their Compensation History
    /// tab. Used by the mutating future-compensation tests below instead of the shared Tom
    /// Williams: Tom is reused by ~40+ other test files (job-title mutation, document/task status,
    /// etc.), so tests that add/edit/delete compensation rows against him race those other tests
    /// under real parallel execution even when the specific dates used don't literally collide.
    /// A fresh employee has no seeded compensation record at all (unlike every seeded Acme
    /// employee including Tom — see EmployeesModule's newHireCompensation seed array), which is
    /// exactly what these "add a future record" tests need to start from.
    /// </summary>
    private async Task<EmployeeEditPage> CreateFreshEmployeeOnCompensationTabAsync(string labelSuffix)
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Comp{labelSuffix}{unique}";
        var workEmail = $"e2e.comp{labelSuffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        await empEdit.OpenCompensationTabAsync();

        return empEdit;
    }

    [Fact]
    public async Task AddCompensation_WithFutureEffectiveDate_AppearsInHistoryWithEditAndDeleteActions()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var empEdit = await CreateFreshEmployeeOnCompensationTabAsync("Add");

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

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var empEdit = await CreateFreshEmployeeOnCompensationTabAsync("Edit");

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

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var empEdit = await CreateFreshEmployeeOnCompensationTabAsync("Delete");

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/12/2030");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("41000");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        await empEdit.ClickDeleteCompensationRowAsync("1 Dec 2030");
        await empEdit.ConfirmDeleteCompensationAsync();

        // ConfirmDeleteCompensationAsync's own wait only confirms the "Yes" confirmation button
        // itself disappeared — that's a separate render pass from the grid actually re-fetching
        // and dropping the deleted row, so a single immediate IsVisibleAsync() snapshot here can
        // still catch it mid-transition. Use an auto-retrying assertion instead.
        await Assertions.Expect(empEdit.CompensationHistoryRow("1 Dec 2030").First)
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
