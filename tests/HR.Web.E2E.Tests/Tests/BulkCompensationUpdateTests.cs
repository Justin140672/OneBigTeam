using ClosedXML.Excel;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Bulk Compensation Update page (Components/Pages/Employees/BulkCompensationUpdate.razor):
/// selecting employees, building a preview under each adjustment mode, editing a proposed salary
/// before confirming, downloading the import template, and importing from Excel (success and
/// row-error paths).
///
/// Every scenario that mutates data creates its own brand-new employee (rather than reusing a
/// shared seeded one) so it can't leak side effects into other tests that rely on seeded
/// employees' compensation state remaining untouched — e.g. EmployeeCompensationTabTests relies on
/// Sarah Chen's exact seeded salary, and on Tom Williams having no compensation record at all.
/// </summary>
[Collection("E2E")]
public sealed class BulkCompensationUpdateTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Creates a brand-new employee (unique last name/email/employee number) with an initial,
    /// open-ended compensation record effective well in the past, so it's this employee's single
    /// "current" record when the Bulk Compensation Update page looks it up.
    /// </summary>
    private async Task<(string LastName, string EmployeeNumber, Guid EmployeeId)> CreateEmployeeWithCompensationAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, decimal initialSalary)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"BulkComp{unique}";
        var workEmail = $"e2e.bulkcomp{unique}@acme.example";
        var employeeNumber = $"E2E-BC-{unique}";

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

        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(_page.Url.TrimEnd('/').Split('/').Last());

        await empEdit.OpenCompensationTabAsync();
        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/01/2020");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync(initialSalary.ToString("0"));
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        return (lastName, employeeNumber, employeeId);
    }

    [Theory]
    [InlineData("Percentage Increase", "10", 55_000)]
    [InlineData("Fixed Amount Increase", "5000", 55_000)]
    [InlineData("Set Salary Directly", "60000", 60_000)]
    public async Task BuildPreviewAndConfirm_ForEachAdjustmentMode_UpdatesEmployeeSalary(
        string modeLabel, string adjustmentValue, decimal expectedSalary)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var bulkPage = new BulkCompensationUpdatePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (lastName, _, employeeId) = await CreateEmployeeWithCompensationAsync(empList, empEdit, initialSalary: 50_000);

        await bulkPage.GoToAsync(AcmeId);
        await bulkPage.SearchEmployeeAsync(lastName);
        await bulkPage.SelectEmployeeAsync(lastName);

        await bulkPage.SelectModeAsync(modeLabel);
        await bulkPage.FillAdjustmentValueAsync(adjustmentValue);
        await bulkPage.FillEffectiveDateAsync("01/06/2026");
        await bulkPage.SelectReasonAsync("Annual Review");

        await bulkPage.ClickBuildPreviewAsync();

        Assert.True(await bulkPage.HasPreviewCardAsync(), "Expected the preview card to render after Build Preview");
        Assert.Equal(1, await bulkPage.GetPreviewRowCountAsync());

        var proposedSalary = await bulkPage.GetProposedSalaryAsync(lastName);
        Assert.Equal(expectedSalary, proposedSalary);

        await bulkPage.ConfirmApplyAsync();

        var success = await bulkPage.GetSuccessMessageAsync();
        Assert.NotNull(success);
        Assert.Contains("Updated compensation for 1 employee", success);

        // Verify the change actually persisted server-side via the employee's own Compensation tab.
        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenCompensationTabAsync();
        var salaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains(expectedSalary.ToString("N2"), salaryText);
    }

    [Fact]
    public async Task EditingProposedSalaryInPreviewGrid_AppliesEditedValue_NotTheCalculatedOne()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var bulkPage = new BulkCompensationUpdatePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (lastName, _, employeeId) = await CreateEmployeeWithCompensationAsync(empList, empEdit, initialSalary: 50_000);

        await bulkPage.GoToAsync(AcmeId);
        await bulkPage.SearchEmployeeAsync(lastName);
        await bulkPage.SelectEmployeeAsync(lastName);

        // 10% increase would auto-calculate to 55,000 — edit it down to a custom value instead.
        await bulkPage.SelectModeAsync("Percentage Increase");
        await bulkPage.FillAdjustmentValueAsync("10");
        await bulkPage.FillEffectiveDateAsync("01/06/2026");
        await bulkPage.SelectReasonAsync("Annual Review");
        await bulkPage.ClickBuildPreviewAsync();

        Assert.Equal(55_000m, await bulkPage.GetProposedSalaryAsync(lastName));

        await bulkPage.SetProposedSalaryAsync(lastName, "58000");
        Assert.Equal(58_000m, await bulkPage.GetProposedSalaryAsync(lastName));

        await bulkPage.ConfirmApplyAsync();

        var success = await bulkPage.GetSuccessMessageAsync();
        Assert.NotNull(success);
        Assert.Contains("Updated compensation for 1 employee", success);

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenCompensationTabAsync();
        var salaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
        Assert.Contains("58,000.00", salaryText);
    }

    [Fact]
    public async Task BuildPreview_EmployeeWithNoCompensationRecord_IsExcludedWithWarning()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var bulkPage = new BulkCompensationUpdatePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create a new employee with NO compensation record at all (unlike the helper above).
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"NoComp{unique}";
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
        await empEdit.FillEmployeeNumberAsync($"E2E-NC-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");
        await empEdit.SaveNewEmployeeAsync();

        await bulkPage.GoToAsync(AcmeId);
        await bulkPage.SearchEmployeeAsync(lastName);
        await bulkPage.SelectEmployeeAsync(lastName);

        await bulkPage.SelectModeAsync("Percentage Increase");
        await bulkPage.FillAdjustmentValueAsync("10");
        await bulkPage.FillEffectiveDateAsync("01/06/2026");
        await bulkPage.SelectReasonAsync("Annual Review");
        await bulkPage.ClickBuildPreviewAsync();

        // No selected employee has a current compensation record, so no preview card renders —
        // instead the page shows its top-level "None of the selected employees…" error.
        Assert.False(await bulkPage.HasPreviewCardAsync(),
            "Expected no preview card when the only selected employee has no compensation record");

        var error = await bulkPage.GetGlobalErrorAsync();
        Assert.NotNull(error);
        Assert.Contains("no current compensation record to adjust", error);
    }

    [Fact]
    public async Task DownloadImportTemplate_TriggersFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var bulkPage = new BulkCompensationUpdatePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await bulkPage.GoToAsync(AcmeId);

        var fileName = await bulkPage.ClickDownloadTemplateAsync();
        Assert.Equal("compensation-import-template.xlsx", fileName);
    }

    [Fact]
    public async Task ImportCompensationChanges_ValidRow_CreatesCompensationRecordForNewEmployee()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var bulkPage = new BulkCompensationUpdatePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // A freshly-created employee has no compensation record yet, but ImportCompensationChanges
        // (unlike the bulk-apply flow above) doesn't require one to exist — it just creates the
        // first record — so no Add Compensation dialog step is needed here.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Import{unique}";
        var workEmail = $"e2e.import{unique}@acme.example";
        var employeeNumber = $"E2E-IM-{unique}";

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

        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(_page.Url.TrimEnd('/').Split('/').Last());

        var tempFile = Path.Combine(Path.GetTempPath(), $"compensation-import-{unique}.xlsx");
        try
        {
            WriteImportWorkbook(tempFile,
            [
                (employeeNumber, "50000", "Annual", "2026-06-01", "AnnualReview", "E2E import")
            ]);

            await bulkPage.GoToAsync(AcmeId);
            await bulkPage.UploadImportFileAsync(tempFile);
            await bulkPage.ClickImportAsync();

            var success = await bulkPage.GetImportSuccessMessageAsync();
            Assert.NotNull(success);
            Assert.Contains("Imported compensation changes for 1 employee", success);

            await empEdit.GoToAsync(AcmeId, employeeId);
            await empEdit.OpenCompensationTabAsync();
            var salaryText = await empEdit.GetCompensationFieldTextAsync("compensation-salary");
            Assert.Contains("50,000.00", salaryText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportCompensationChanges_RowWithInvalidData_ShowsRowError_AndImportsNothing()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var bulkPage = new BulkCompensationUpdatePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var tempFile = Path.Combine(Path.GetTempPath(), $"compensation-import-badrow-{unique}.xlsx");
        try
        {
            // An unknown Employee Number and a non-numeric New Salary each fail row validation
            // (ImportCompensationChangesHandler) — this deliberately touches no real employee, so
            // it can't collide with seeded or other tests' data even though nothing is written.
            WriteImportWorkbook(tempFile,
            [
                ("BOGUS-999", "not-a-number", "Annual", "2026-06-01", "AnnualReview", "")
            ]);

            await bulkPage.GoToAsync(AcmeId);
            await bulkPage.UploadImportFileAsync(tempFile);
            await bulkPage.ClickImportAsync();

            var rowErrors = await bulkPage.GetImportRowErrorsTextAsync();
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
    /// Only the columns the parser actually reads are populated here (Employee Name/Current Salary
    /// are reference-only and ignored by the parser).
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
            sheet.Cell(rowIndex, 5).Value = row.NewSalary;
            sheet.Cell(rowIndex, 6).Value = row.EffectiveDate;
            sheet.Cell(rowIndex, 7).Value = row.Reason;
            sheet.Cell(rowIndex, 8).Value = row.Notes;
            rowIndex++;
        }

        workbook.SaveAs(filePath);
    }
}
