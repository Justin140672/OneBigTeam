using ClosedXML.Excel;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Employee List's multi-row selection + "Bulk Update" toolbar button
/// (Components/Pages/Employees/EmployeeList.razor), which opens BulkCompensationUpdateDialog.razor
/// wrapping the shared BulkCompensationAdjustmentPanel.razor for compensation bulk adjustments, and
/// the "Import" dropdown item which opens BulkCompensationImportDialog.razor wrapping
/// BulkCompensationImportPanel.razor. This is the sole E2E coverage for bulk compensation
/// adjustments/imports — the old standalone full-page Bulk Compensation Update screen (and its
/// tests) has been removed now that everything it did is reachable from these dialogs.
///
/// Every scenario that mutates data creates its own brand-new employee(s) so it can't leak side
/// effects into other tests that rely on seeded employees' compensation state remaining untouched.
/// </summary>
public sealed class EmployeeListBulkUpdateTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee (unique last name/email/employee number) with an initial,
    /// open-ended compensation record effective well in the past, so it's this employee's single
    /// "current" record when the bulk update dialog looks it up. Mirrors
    /// BulkCompensationUpdateTests.CreateEmployeeWithCompensationAsync.
    /// </summary>
    private async Task<(string LastName, Guid EmployeeId)> CreateEmployeeWithCompensationAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string uniqueSuffix, decimal initialSalary)
    {
        var lastName = $"BulkListComp{uniqueSuffix}";
        var workEmail = $"e2e.bulklistcomp{uniqueSuffix}@acme.example";
        var employeeNumber = $"E2E-BLC-{uniqueSuffix}";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync(employeeNumber);
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");
        await empEdit.SaveNewEmployeeAsync();

        // Matches the guid directly rather than splitting on the trailing "/view" segment
        // (EmployeeList.razor's row link/OnRecordClick lands on the view route).
        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(System.Text.RegularExpressions.Regex.Match(
            _page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})").Groups[1].Value);

        await empEdit.OpenCompensationTabAsync();
        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/01/2020");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync(initialSalary.ToString("0"));
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        return (lastName, employeeId);
    }

    [Fact]
    public async Task BulkUpdateButton_IsDisabledWithNoSelection_AndEnabledWithSelection()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var (lastName, _) = await CreateEmployeeWithCompensationAsync(empList, empEdit, unique, initialSalary: 50_000);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);

        Assert.True(await empList.IsBulkUpdateButtonDisabledAsync(),
            "Expected the Bulk Update button to be disabled with zero rows selected");

        await empList.CheckEmployeeRowAsync(lastName);

        Assert.False(await empList.IsBulkUpdateButtonDisabledAsync(),
            "Expected the Bulk Update button to be enabled once a row is selected");
    }

    [Fact]
    public async Task BulkUpdate_TwoSelectedEmployees_AppliesAdjustmentToBoth()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new BulkCompensationUpdateDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var (firstLastName, firstId) =
            await CreateEmployeeWithCompensationAsync(empList, empEdit, $"{unique}A", initialSalary: 50_000);
        var (secondLastName, secondId) =
            await CreateEmployeeWithCompensationAsync(empList, empEdit, $"{unique}B", initialSalary: 80_000);

        // Both new employees share the "BulkListComp{unique}" prefix, so a single search surfaces
        // both rows for multi-selection.
        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync($"BulkListComp{unique}");

        await empList.CheckEmployeeRowAsync(firstLastName);
        await empList.CheckEmployeeRowAsync(secondLastName);

        Assert.False(await empList.IsBulkUpdateButtonDisabledAsync(),
            "Expected the Bulk Update button to be enabled with two rows selected");

        await empList.ClickBulkUpdateAsync();

        var summary = await dialog.GetSelectedEmployeesSummaryAsync();
        Assert.NotNull(summary);
        Assert.Contains("2 selected employee", summary);

        await dialog.SelectModeAsync("Percentage Increase");
        await dialog.FillAdjustmentValueAsync("10");
        await dialog.FillEffectiveDateAsync("01/06/2026");
        await dialog.SelectReasonAsync("Annual Review");

        await dialog.ClickBuildPreviewAsync();

        Assert.True(await dialog.HasPreviewCardAsync(), "Expected the preview card to render after Build Preview");
        Assert.Equal(2, await dialog.GetPreviewRowCountAsync());

        Assert.Equal(55_000m, await dialog.GetProposedSalaryAsync(firstLastName));
        Assert.Equal(88_000m, await dialog.GetProposedSalaryAsync(secondLastName));

        await dialog.ConfirmApplyAsync();

        var success = await empList.GetActionSuccessMessageAsync();
        Assert.NotNull(success);
        Assert.Contains("Updated compensation for 2 employee", success);

        // Verify the change actually persisted server-side via each employee's own Compensation tab.
        await empEdit.GoToAsync(AcmeId, firstId);
        await empEdit.OpenCompensationTabAsync();
        var firstSalaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains(55_000m.ToString("N2"), firstSalaryText);

        await empEdit.GoToAsync(AcmeId, secondId);
        await empEdit.OpenCompensationTabAsync();
        var secondSalaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains(88_000m.ToString("N2"), secondSalaryText);
    }

    [Theory]
    [InlineData("Fixed Amount Increase", "5000", 55_000)]
    [InlineData("Set Salary Directly", "60000", 60_000)]
    public async Task BulkUpdate_ForEachAdjustmentMode_UpdatesEmployeeSalary(
        string modeLabel, string adjustmentValue, decimal expectedSalary)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new BulkCompensationUpdateDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var (lastName, employeeId) = await CreateEmployeeWithCompensationAsync(empList, empEdit, unique, initialSalary: 50_000);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);
        await empList.CheckEmployeeRowAsync(lastName);
        await empList.ClickBulkUpdateAsync();

        await dialog.SelectModeAsync(modeLabel);
        await dialog.FillAdjustmentValueAsync(adjustmentValue);
        await dialog.FillEffectiveDateAsync("01/06/2026");
        await dialog.SelectReasonAsync("Annual Review");

        await dialog.ClickBuildPreviewAsync();

        Assert.True(await dialog.HasPreviewCardAsync(), "Expected the preview card to render after Build Preview");
        Assert.Equal(1, await dialog.GetPreviewRowCountAsync());

        var proposedSalary = await dialog.GetProposedSalaryAsync(lastName);
        Assert.Equal(expectedSalary, proposedSalary);

        await dialog.ConfirmApplyAsync();

        var success = await empList.GetActionSuccessMessageAsync();
        Assert.NotNull(success);
        Assert.Contains("Updated compensation for 1 employee", success);

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenCompensationTabAsync();
        var salaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains(expectedSalary.ToString("N2"), salaryText);
    }

    [Fact]
    public async Task BulkUpdate_EditingProposedSalaryInPreviewGrid_AppliesEditedValue_NotTheCalculatedOne()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new BulkCompensationUpdateDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var (lastName, employeeId) = await CreateEmployeeWithCompensationAsync(empList, empEdit, unique, initialSalary: 50_000);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);
        await empList.CheckEmployeeRowAsync(lastName);
        await empList.ClickBulkUpdateAsync();

        // 10% increase would auto-calculate to 55,000 — edit it down to a custom value instead.
        await dialog.SelectModeAsync("Percentage Increase");
        await dialog.FillAdjustmentValueAsync("10");
        await dialog.FillEffectiveDateAsync("01/06/2026");
        await dialog.SelectReasonAsync("Annual Review");
        await dialog.ClickBuildPreviewAsync();

        Assert.Equal(55_000m, await dialog.GetProposedSalaryAsync(lastName));

        await dialog.SetProposedSalaryAsync(lastName, "58000");
        Assert.Equal(58_000m, await dialog.GetProposedSalaryAsync(lastName));

        await dialog.ConfirmApplyAsync();

        var success = await empList.GetActionSuccessMessageAsync();
        Assert.NotNull(success);
        Assert.Contains("Updated compensation for 1 employee", success);

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenCompensationTabAsync();
        var salaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains("58,000.00", salaryText);
    }

    [Fact]
    public async Task BulkUpdate_EmployeeWithNoCompensationRecord_IsExcludedWithWarning()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new BulkCompensationUpdateDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create a new employee with NO compensation record at all.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"BulkListNoComp{unique}";
        var workEmail = $"e2e.bulklistnocomp{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-BLN-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");
        await empEdit.SaveNewEmployeeAsync();

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);
        await empList.CheckEmployeeRowAsync(lastName);
        await empList.ClickBulkUpdateAsync();

        await dialog.SelectModeAsync("Percentage Increase");
        await dialog.FillAdjustmentValueAsync("10");
        await dialog.FillEffectiveDateAsync("01/06/2026");
        await dialog.SelectReasonAsync("Annual Review");
        await dialog.ClickBuildPreviewAsync();

        // No selected employee has a current compensation record, so no preview card renders —
        // instead the panel shows its own "None of the selected employees…" error.
        Assert.False(await dialog.HasPreviewCardAsync(),
            "Expected no preview card when the only selected employee has no compensation record");

        var error = await dialog.GetGlobalErrorAsync();
        Assert.NotNull(error);
        Assert.Contains("None of the selected employees have a current compensation record to adjust.", error);
    }

    [Fact]
    public async Task DownloadTemplate_FromBulkUpdateDropdown_TriggersFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);

        var fileName = await empList.ClickDownloadTemplateAsync();
        Assert.Equal("compensation-import-template.xlsx", fileName);

        // Downloading shouldn't surface any error banner on the list page.
        Assert.Null(await empList.GetActionErrorMessageAsync());
    }

    // ImportCompensationChangesHandler intentionally requires an existing open compensation
    // record to import into — Salary Frequency is never taken from the import row itself, only
    // inherited from that existing record (see the handler's own comment: "Employees with no
    // existing open compensation record ... cannot be processed via this import"). So unlike the
    // other Import_* tests below, this one must give the employee an initial compensation record
    // via the UI first (CreateEmployeeWithCompensationAsync) — a brand-new hire with no
    // compensation at all is correctly rejected by the API, not a bug in the import feature.
    [Fact]
    public async Task Import_ValidRow_FromBulkUpdateDropdown_UpdatesCompensationRecordForEmployee()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var importDialog = new BulkCompensationImportDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var (_, employeeId) = await CreateEmployeeWithCompensationAsync(empList, empEdit, unique, 50000);

        // Acme's Employee Number Mode is shared, mutable company state — other test classes
        // (HrSettingsPageTests, BackfillEmployeeNumbersTests) flip it between Manual and
        // Automatic via the UI, so it can't be assumed here. CreateEmployeeWithCompensationAsync's
        // FillEmployeeNumberAsync is a documented no-op in Automatic mode (see its own doc
        // comment), in which case the employee's real number is one the backend generated, not
        // the local `E2E-BLC-{unique}` string. Read the actually-assigned number back from the
        // page instead of assuming it matches what was typed, so this test passes regardless of
        // the ambient mode.
        var assignedEmployeeNumber = (await empEdit.GetEmployeeNumberHeaderTextAsync())?.TrimStart('#')
            ?? $"E2E-BLC-{unique}";

        var tempFile = Path.Combine(Path.GetTempPath(), $"compensation-import-list-{unique}.xlsx");
        try
        {
            WriteImportWorkbook(tempFile,
            [
                (assignedEmployeeNumber, "60000", "Annual", "2026-06-01", "AnnualReview", "E2E list import")
            ]);

            await empList.GoToAsync(AcmeId);
            await empList.ClickBulkImportAsync();
            await importDialog.UploadImportFileAsync(tempFile);
            await importDialog.ClickImportAsync();

            var success = await empList.GetActionSuccessMessageAsync();
            Assert.NotNull(success);
            Assert.Contains("Imported compensation changes for 1 employee", success);

            await empEdit.GoToAsync(AcmeId, employeeId);
            await empEdit.OpenCompensationTabAsync();
            var salaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
            Assert.Contains("60,000.00", salaryText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Import_RowWithInvalidData_FromBulkUpdateDropdown_ShowsRowError_AndImportsNothing()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var importDialog = new BulkCompensationImportDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var tempFile = Path.Combine(Path.GetTempPath(), $"compensation-import-list-badrow-{unique}.xlsx");
        try
        {
            WriteImportWorkbook(tempFile,
            [
                ("BOGUS-999", "not-a-number", "Annual", "2026-06-01", "AnnualReview", "")
            ]);

            await empList.GoToAsync(AcmeId);
            await empList.ClickBulkImportAsync();
            await importDialog.UploadImportFileAsync(tempFile);
            await importDialog.ClickImportAsync();

            var rowErrors = await importDialog.GetRowErrorsTextAsync();
            Assert.NotNull(rowErrors);
            Assert.Contains("Row 2", rowErrors);
            Assert.Contains("was not found", rowErrors);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Writes a compensation-import .xlsx workbook matching the columns produced by
    /// CompensationImportTemplateBuilder / read by CompensationImportFileParser: Employee Number,
    /// Employee Name, Current Salary, Salary Frequency, New Salary, Effective Date, Reason, Notes.
    /// Mirrors BulkCompensationUpdateTests.WriteImportWorkbook for the same coverage against the
    /// Employee List's own Import entry point.
    /// </summary>
    private static void WriteImportWorkbook(
        string filePath,
        (string EmployeeNumber, string NewSalary, string SalaryFrequency, string EffectiveDate, string Reason, string Notes)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Compensation Import");

        string[] headers =
        [
            "Employee Number", "Employee Name", "Current Salary", "Salary Frequency",
            "New Salary", "Effective Date", "Reason", "Notes"
        ];
        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.EmployeeNumber;
            sheet.Cell(rowIndex, 4).Value = row.SalaryFrequency;
            sheet.Cell(rowIndex, 5).Value = row.NewSalary;
            sheet.Cell(rowIndex, 6).Value = row.EffectiveDate;
            sheet.Cell(rowIndex, 7).Value = row.Reason;
            sheet.Cell(rowIndex, 8).Value = row.Notes;
            rowIndex++;
        }

        workbook.SaveAs(filePath);
    }
}
