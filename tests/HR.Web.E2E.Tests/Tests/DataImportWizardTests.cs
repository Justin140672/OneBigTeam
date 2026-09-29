using ClosedXML.Excel;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DataImportWizardTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task UploadValidateConfirm_CreatesEmployees()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workEmail = $"e2e.import.{suffix}@example.com";

        string[] headers =
        [
            "First Name", "Last Name", "Work Email", "Date Of Birth", "Nationality", "Gender",
            "Start Date", "Department", "Location", "Employment Type",
            "Position Profile", "Salary Amount"
        ];
        string[][] rows =
        [
            ["Imported", "Employee", workEmail, "1990-06-15", "British", "Male", "2026-01-01",
                "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"]
        ];

        var tempFile = Path.Combine(Path.GetTempPath(), $"employee-import-{suffix}.xlsx");
        WriteImportWorkbook(tempFile, headers, rows);

        try
        {
            var login      = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard     = new DataImportWizardPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);

            var firstNameMapping = await wizard.GetMappingSelectionAsync("First Name");
            Assert.Equal("First Name", firstNameMapping);

            await wizard.ContinueFromMappingAsync();
            await wizard.ViewPreviewAsync();

            Assert.True(await wizard.HasValidRowAsync(workEmail),
                $"Expected the preview grid to show the uploaded row for '{workEmail}'");

            await wizard.ConfirmImportAsync();

            var status = await wizard.GetResultStatusAsync();
            Assert.Equal("Imported", status);

            var createdCount = await wizard.GetCreatedCountAsync();
            Assert.Equal(1, createdCount);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadTemplate_BeforeUpload_DownloadsTemplateFile()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var wizard = new DataImportWizardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await wizard.GoToAsync(AcmeId);

        var fileName = await wizard.ClickDownloadTemplateAsync();

        Assert.Contains("template", fileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_WithInvalidRow_AllowsDownloadingErrorReport()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var validEmail = $"e2e.importok.{suffix}@example.com";

        string[] headers =
        [
            "First Name", "Last Name", "Work Email", "Date Of Birth", "Nationality", "Gender",
            "Start Date", "Department", "Location", "Employment Type",
            "Position Profile", "Salary Amount"
        ];
        string[][] rows =
        [
            ["Valid", "Employee", validEmail, "1990-06-15", "British", "Male", "2026-01-01",
                "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"],
            ["Invalid", "", $"e2e.importbad.{suffix}@example.com", "1990-06-15", "British", "Male",
                "2026-01-01", "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"]
        ];

        var tempFile = Path.Combine(Path.GetTempPath(), $"employee-import-errors-{suffix}.xlsx");
        WriteImportWorkbook(tempFile, headers, rows);

        try
        {
            var login  = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard = new DataImportWizardPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);
            await wizard.ContinueFromMappingAsync();
            await wizard.ViewPreviewAsync();

            var fileName = await wizard.ClickDownloadErrorReportAsync();

            Assert.Contains("errors", fileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Preview_WithMissingSalaryAmount_ProducesRowError()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var validEmail = $"e2e.importsalok.{suffix}@example.com";
        var invalidEmail = $"e2e.importsalbad.{suffix}@example.com";

        string[] headers =
        [
            "First Name", "Last Name", "Work Email", "Date Of Birth", "Nationality", "Gender",
            "Start Date", "Department", "Location", "Employment Type",
            "Position Profile", "Salary Amount"
        ];
        string[][] rows =
        [
            ["Valid", "Employee", validEmail, "1990-06-15", "British", "Male", "2026-01-01",
                "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", "45000"],
            ["Invalid", "Employee", invalidEmail, "1990-06-15", "British", "Male", "2026-01-01",
                "Engineering", "London Office", "Permanent",
                "Senior Software Engineer", ""]
        ];

        var tempFile = Path.Combine(Path.GetTempPath(), $"employee-import-salary-{suffix}.xlsx");
        WriteImportWorkbook(tempFile, headers, rows);

        try
        {
            var login  = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard = new DataImportWizardPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);
            await wizard.ContinueFromMappingAsync();
            await wizard.ViewPreviewAsync();

            var fileName = await wizard.ClickDownloadErrorReportAsync();

            Assert.Contains("errors", fileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

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
