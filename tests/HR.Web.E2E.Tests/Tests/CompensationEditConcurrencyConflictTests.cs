using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the "Edit Future Compensation" dialog
/// (EditFutureCompensationDialog.razor), opened from the Compensation History grid's per-row
/// "Edit" action on a future-dated record.
///
/// When the dialog's save is rejected with HTTP 409 because the record's Version moved on since
/// the dialog loaded it (ExpectedVersion stale), the dialog renders the shared
/// &lt;SaveConflictBanner&gt; ("Someone else changed this compensation record while you were
/// editing. Your changes have not been saved.") plus a "Reload latest values" button. The dialog
/// stays open and the first editor's entered values are preserved. "Reload latest values"
/// re-fetches the record, repopulates the form with the competing editor's values, adopts the
/// fresh Version and clears the banner — after which a re-save succeeds.
///
/// The "second editor" is a second browser tab in the same authenticated HR-admin context that
/// opens the same row's Edit dialog and saves first, bumping the record's Version.
///
/// Uses a fresh, uniquely-named employee (same approach as EmployeeCompensationTabTests' mutating
/// future-compensation tests) rather than a shared seeded employee: a fresh employee has no
/// compensation record contended by the ~40+ other parallel test files, and these tests need to
/// add their own future-dated record to edit anyway.
/// </summary>
public sealed class CompensationEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private const string EffectiveFrom = "01/01/2030";
    private const string RowFragment   = "1 Jan 2030";

    [Fact]
    public async Task EditFutureCompensation_SaveAfterAnotherActorChangedRecord_ShowsConflictBanner_ThenReloadRecovers()
    {
        const decimal originalSalary = 40000m;
        const decimal otherTabSalary = 55000m;
        const decimal finalSalary    = 60000m;

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Arrange: fresh employee with a single future-dated compensation record ──
        var empEdit = await CreateFreshEmployeeOnCompensationTabAsync();

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync(EffectiveFrom);
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync(originalSalary.ToString("0"));
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        var employeeId = ExtractEmployeeId(_page.Url);

        // ── Tab 1: open the Edit dialog for the future record (loads Version v1) ──
        await empEdit.ClickEditCompensationRowAsync(RowFragment);
        var dialog = new EditFutureCompensationDialog(_page);
        await dialog.WaitForOpenAsync();
        await dialog.FillSalaryAsync(finalSalary);

        // ── Tab 2 (same context / persona): open the same row's Edit dialog and save first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherEmpEdit = new EmployeeEditPage(otherPage, _fixture.WebBaseUrl);
            await otherEmpEdit.GoToAsync(AcmeId, employeeId);
            await otherEmpEdit.OpenCompensationTabAsync();
            await otherEmpEdit.ClickEditCompensationRowAsync(RowFragment);

            var otherDialog = new EditFutureCompensationDialog(otherPage);
            await otherDialog.WaitForOpenAsync();
            await otherDialog.FillSalaryAsync(otherTabSalary);
            await otherDialog.SubmitExpectingSuccessAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, dialog stays open, input preserved ──
        await dialog.SubmitExpectingConflictAsync();

        Assert.True(await dialog.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale save");
        Assert.True(await dialog.IsOpenAsync(),
            "The Edit Future Compensation dialog should stay open after a concurrency conflict");
        Assert.Equal(finalSalary, await dialog.GetSalaryValueAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
        await dialog.ClickReloadLatestValuesAsync();

        Assert.False(await dialog.IsConcurrencyWarningVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal(otherTabSalary, await dialog.GetSalaryValueAsync());

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await dialog.FillSalaryAsync(finalSalary);
        await dialog.SubmitExpectingSuccessAsync();

        Assert.False(await dialog.IsOpenAsync(),
            "Expected the dialog to close after a successful save against the reloaded version");

        var rowText = await empEdit.CompensationHistoryRow(RowFragment).First.TextContentAsync();
        Assert.Contains("60,000.00", rowText);
    }

    /// <summary>
    /// Creates a fresh, uniquely-named Acme employee and lands on their Compensation History tab.
    /// Mirrors EmployeeCompensationTabTests.CreateFreshEmployeeOnCompensationTabAsync — a fresh
    /// employee has no seeded compensation record and is not contended by other parallel tests.
    /// </summary>
    private async Task<EmployeeEditPage> CreateFreshEmployeeOnCompensationTabAsync()
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"CompConflict{unique}";
        var workEmail = $"e2e.compconflict{unique}@acme.example";

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
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        await empEdit.OpenCompensationTabAsync();

        return empEdit;
    }

    /// <summary>
    /// Pulls the employee GUID out of the current /companies/{companyId}/employees/{employeeId}
    /// URL (optionally suffixed with /view or a query string) so the second tab can navigate
    /// straight to the same employee.
    /// </summary>
    private static Guid ExtractEmployeeId(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            url, @"/employees/(?<id>[0-9a-fA-F-]{36})");
        Assert.True(match.Success, $"Could not extract an employee id from URL '{url}'.");
        return Guid.Parse(match.Groups["id"].Value);
    }
}
