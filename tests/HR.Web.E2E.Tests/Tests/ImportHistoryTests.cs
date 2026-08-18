using ClosedXML.Excel;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a completed employee data import appears in the import history list, and that
/// its session detail page shows the completed status and row counts.
/// </summary>
public sealed class ImportHistoryTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompletedImport_AppearsInHistory_AndDetailShowsStatusAndRowCounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workEmail = $"e2e.history.{suffix}@example.com";
        var employeeNumber = $"E2EH{suffix}";
        var fileName = $"e2e-history-{suffix}.xlsx";

        // Date Of Birth, Nationality, Gender, Salary Amount, Department, Location, Employment
        // Type, and Position Profile are all required by EmployeeStagingRowValidator.RequiredFields/
        // RequiredLookupFields alongside First Name/Last Name/Work Email/Start Date/Employee
        // Number — omitting any of them fails the row before it ever reaches
        // ConfirmImportSessionHandler. Department/Location/Employment Type/Position Profile are
        // resolved by name (auto-created if they don't already exist), so using the seeded
        // "Engineering"/"London Office"/"Senior Software Engineer" values (see
        // CreateEmployeeTests) avoids an unnecessary auto-create warning.
        string[] headers =
        [
            "First Name", "Last Name", "Work Email", "Date Of Birth", "Nationality", "Gender",
            "Start Date", "Employee Number", "Department", "Location", "Employment Type",
            "Position Profile", "Salary Amount"
        ];
        string[][] rows =
        [
            ["History", "Employee", workEmail, "1990-06-15", "British", "Male", "2026-01-01",
                employeeNumber, "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"]
        ];

        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        WriteImportWorkbook(tempFile, headers, rows);

        try
        {
            var login      = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard     = new DataImportWizardPage(_page, _fixture.WebBaseUrl);
            var history    = new ImportHistoryPage(_page, _fixture.WebBaseUrl);
            var detail     = new ImportSessionDetailPage(_page, _fixture.WebBaseUrl);
            var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);
            await EnsureManualEmployeeNumberModeAsync(hrSettings);

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);
            await wizard.ContinueFromMappingAsync();
            await wizard.ViewPreviewAsync();
            await wizard.ConfirmImportAsync();

            var wizardStatus = await wizard.GetResultStatusAsync();
            Assert.Equal("Imported", wizardStatus);

            await history.GoToAsync(AcmeId);

            Assert.True(await history.HasSessionAsync(fileName),
                $"Expected the completed import session for '{fileName}' to appear in import history");

            // Column order per ImportHistory.razor: 0=File Name, 1=Status, 2=Total Rows,
            // 3=Successful Rows, 4=Failed Rows.
            Assert.Equal("1", await history.GetRowCellAsync(fileName, 2));
            Assert.Equal("1", await history.GetRowCellAsync(fileName, 3));
            Assert.Equal("0", await history.GetRowCellAsync(fileName, 4));

            await history.OpenSessionAsync(fileName);

            Assert.Contains("/data-import/sessions/", _page.Url);

            var detailStatus = await detail.GetStatusAsync();
            Assert.Equal("Imported", detailStatus);

            Assert.Equal("1", await detail.GetDetailAsync("Total Rows"));
            Assert.Equal("1", await detail.GetDetailAsync("Successful Rows"));
            Assert.Equal("0", await detail.GetDetailAsync("Failed Rows"));
            Assert.False(await detail.IsDownloadErrorReportButtonVisibleAsync(),
                "Download Error Report should not be shown when there are no failed rows");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PartiallyFailedImport_ShowsFailedRowCount_AndErrorReportDownload()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workEmail = $"e2e.historyfail.{suffix}@example.com";
        var employeeNumber = $"E2EHF{suffix}";
        var fileName = $"e2e-history-fail-{suffix}.xlsx";

        // Second row is missing the required Last Name — one valid row, one failed row. Date Of
        // Birth/Nationality/Gender/Salary Amount/Department/Location/Employment Type/Position
        // Profile are also required (see
        // EmployeeStagingRowValidator.RequiredFields/RequiredLookupFields) and are included on
        // both rows so Last Name is the only thing that fails the second one.
        string[] headers =
        [
            "First Name", "Last Name", "Work Email", "Date Of Birth", "Nationality", "Gender",
            "Start Date", "Employee Number", "Department", "Location", "Employment Type",
            "Position Profile", "Salary Amount"
        ];
        string[][] rows =
        [
            ["Valid", "Employee", workEmail, "1990-06-15", "British", "Male", "2026-01-01",
                employeeNumber, "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"],
            ["Invalid", "", $"invalid.{suffix}@example.com", "1990-06-15", "British", "Male",
                "2026-01-02", $"E2EHFX{suffix}", "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"]
        ];

        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        WriteImportWorkbook(tempFile, headers, rows);

        try
        {
            var login      = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard     = new DataImportWizardPage(_page, _fixture.WebBaseUrl);
            var history    = new ImportHistoryPage(_page, _fixture.WebBaseUrl);
            var detail     = new ImportSessionDetailPage(_page, _fixture.WebBaseUrl);
            var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);
            await EnsureManualEmployeeNumberModeAsync(hrSettings);

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);
            await wizard.ContinueFromMappingAsync();
            await wizard.ViewPreviewAsync();
            await wizard.ConfirmImportAsync();

            var wizardStatus = await wizard.GetResultStatusAsync();
            Assert.Equal("CompletedWithErrors", wizardStatus);

            await history.GoToAsync(AcmeId);

            Assert.Equal("2", await history.GetRowCellAsync(fileName, 2));
            Assert.Equal("1", await history.GetRowCellAsync(fileName, 3));
            Assert.Equal("1", await history.GetRowCellAsync(fileName, 4));

            await history.OpenSessionAsync(fileName);

            Assert.Equal("1", await detail.GetDetailAsync("Successful Rows"));
            Assert.Equal("1", await detail.GetDetailAsync("Failed Rows"));
            Assert.True(await detail.IsDownloadErrorReportButtonVisibleAsync(),
                "Download Error Report should be shown when there are failed rows");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // Acme's Employee Number Mode is shared, mutable company state that other test classes
    // (HrSettingsPageTests, BackfillEmployeeNumbersTests, etc.) flip between Manual and
    // Automatic via the UI. Both tests in this file supply an explicit "Employee Number" import
    // column value, which EmployeeStagingRowValidator rejects outright when the company is in
    // Automatic mode (see ValidateEmployeeNumberField) — so set the mode deterministically
    // rather than assuming whatever an earlier test happened to leave it as. Same fix as
    // DataImportWizardTests.UploadValidateConfirm_CreatesEmployees.
    private async Task EnsureManualEmployeeNumberModeAsync(HrSettingsPage hrSettings)
    {
        await hrSettings.GoToAsync(AcmeId);
        if (await hrSettings.GetEmployeeNumberModeAsync() != "Manual")
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Manual");
            await hrSettings.SaveAsync();
        }
    }

    // EmployeeImportFileParser (HR.Modules.DataImport) reads uploaded files as an .xlsx workbook
    // via ClosedXML's XLWorkbook — there is no CSV code path, so a plain-text CSV (even with a
    // .csv extension) fails to parse as a workbook at all. Builds a minimal single-sheet workbook
    // with the given headers and rows, mirroring EmployeeListBulkUpdateTests.WriteImportWorkbook's
    // approach for the same reason.
    private static void WriteImportWorkbook(string filePath, string[] headers, string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Employees");

        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var col = 0; col < row.Length; col++)
                sheet.Cell(rowIndex + 2, col + 1).Value = row[col];
        }

        workbook.SaveAs(filePath);
    }
}
