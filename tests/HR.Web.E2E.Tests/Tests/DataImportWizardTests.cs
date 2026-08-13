using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the employee data import wizard end-to-end flow: upload a CSV, confirm the
/// auto-detected column mapping, validate it, view the row preview, and confirm the import to
/// create employee records. Also covers the Download Template and Download Error Report
/// affordances on the Upload and Preview &amp; Confirm steps.
/// </summary>
[Collection("E2E")]
public sealed class DataImportWizardTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task UploadValidateConfirm_CreatesEmployees()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workEmail = $"e2e.import.{suffix}@example.com";
        var employeeNumber = $"E2E{suffix}";

        // Date Of Birth, Nationality, Gender, Department, Location, Employment Type, and Position
        // Profile are all required by EmployeeStagingRowValidator.RequiredFields/
        // RequiredLookupFields (src/Modules/HR.Modules.DataImport/Services/
        // EmployeeStagingRowValidator.cs) — omitting any of them fails the row before it ever
        // reaches ConfirmImportSessionHandler. Department/Location/Employment Type/Position
        // Profile are resolved by name (auto-created if they don't already exist), so using the
        // seeded "Engineering"/"London Office"/"Senior Software Engineer" values (see
        // CreateEmployeeTests) avoids an unnecessary auto-create warning.
        var csvContent =
            "First Name,Last Name,Work Email,Date Of Birth,Nationality,Gender,Start Date,Employee Number,Department,Location,Employment Type,Position Profile\n" +
            $"Imported,Employee,{workEmail},1990-06-15,British,Male,2026-01-01,{employeeNumber},Engineering,London Office,Permanent,Senior Software Engineer\n";

        var tempFile = Path.Combine(Path.GetTempPath(), $"employee-import-{suffix}.csv");
        await File.WriteAllTextAsync(tempFile, csvContent);

        try
        {
            var login      = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard     = new DataImportWizardPage(_page, _fixture.WebBaseUrl);
            var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

            // Acme's Employee Number Mode is shared, mutable company state that other test
            // classes (HrSettingsPageTests, BackfillEmployeeNumbersTests, etc.) flip between
            // Manual and Automatic via the UI. This test's CSV row supplies an explicit
            // "Employee Number" value, which EmployeeStagingRowValidator rejects outright when
            // the company is in Automatic mode (see ValidateEmployeeNumberField) — so set the
            // mode deterministically rather than assuming whatever an earlier test happened to
            // leave it as.
            await hrSettings.GoToAsync(AcmeId);
            if (await hrSettings.GetEmployeeNumberModeAsync() != "Manual")
            {
                await hrSettings.SelectEmployeeNumberModeAsync("Manual");
                await hrSettings.SaveAsync();
            }

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);

            // The Column Mapping step auto-detects the file's headers and pre-suggests a
            // mapping; since the uploaded CSV's headers match the standard field names exactly,
            // the "First Name" row's dropdown should already be selected to "First Name".
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
        var validEmployeeNumber = $"E2EOK{suffix}";

        // The second row is missing a Last Name (a required field), which should produce a row
        // error surfaced on the Preview & Confirm step. Date Of Birth/Nationality/Gender/
        // Department/Location/Employment Type/Position Profile are also required (see
        // EmployeeStagingRowValidator) and are included on both rows so Last Name is the only
        // thing that fails the second one.
        var csvContent =
            "First Name,Last Name,Work Email,Date Of Birth,Nationality,Gender,Start Date,Employee Number,Department,Location,Employment Type,Position Profile\n" +
            $"Valid,Employee,{validEmail},1990-06-15,British,Male,2026-01-01,{validEmployeeNumber},Engineering,London Office,Permanent,Senior Software Engineer\n" +
            $"Invalid,,e2e.importbad.{suffix}@example.com,1990-06-15,British,Male,2026-01-01,E2EBAD{suffix},Engineering,London Office,Permanent,Senior Software Engineer\n";

        var tempFile = Path.Combine(Path.GetTempPath(), $"employee-import-errors-{suffix}.csv");
        await File.WriteAllTextAsync(tempFile, csvContent);

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
}
