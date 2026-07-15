using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a completed employee data import appears in the import history list, and that
/// its session detail page shows the completed status and row counts.
/// </summary>
[Collection("E2E")]
public sealed class ImportHistoryTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompletedImport_AppearsInHistory_AndDetailShowsStatusAndRowCounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workEmail = $"e2e.history.{suffix}@example.com";
        var employeeNumber = $"E2EH{suffix}";
        var fileName = $"e2e-history-{suffix}.csv";

        // Date Of Birth, Nationality, Gender, Department, Location, Employment Type, and Position
        // Profile are all required by EmployeeStagingRowValidator.RequiredFields/
        // RequiredLookupFields alongside First Name/Last Name/Work Email/Start Date/Employee
        // Number — omitting any of them fails the row before it ever reaches
        // ConfirmImportSessionHandler. Department/Location/Employment Type/Position Profile are
        // resolved by name (auto-created if they don't already exist), so using the seeded
        // "Engineering"/"London Office"/"Senior Software Engineer" values (see
        // CreateEmployeeTests) avoids an unnecessary auto-create warning.
        var csvContent =
            "First Name,Last Name,Work Email,Date Of Birth,Nationality,Gender,Start Date,Employee Number,Department,Location,Employment Type,Position Profile\n" +
            $"History,Employee,{workEmail},1990-06-15,British,Male,2026-01-01,{employeeNumber},Engineering,London Office,Permanent,Senior Software Engineer\n";

        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllTextAsync(tempFile, csvContent);

        try
        {
            var login   = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard  = new DataImportWizardPage(_page, _fixture.WebBaseUrl);
            var history = new ImportHistoryPage(_page, _fixture.WebBaseUrl);
            var detail  = new ImportSessionDetailPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

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
        var fileName = $"e2e-history-fail-{suffix}.csv";

        // Second row is missing the required Last Name — one valid row, one failed row. Date Of
        // Birth/Nationality/Gender/Department/Location/Employment Type/Position Profile are also
        // required (see EmployeeStagingRowValidator.RequiredFields/RequiredLookupFields) and are
        // included on both rows so Last Name is the only thing that fails the second one.
        var csvContent =
            "First Name,Last Name,Work Email,Date Of Birth,Nationality,Gender,Start Date,Employee Number,Department,Location,Employment Type,Position Profile\n" +
            $"Valid,Employee,{workEmail},1990-06-15,British,Male,2026-01-01,{employeeNumber},Engineering,London Office,Permanent,Senior Software Engineer\n" +
            $"Invalid,,invalid.{suffix}@example.com,1990-06-15,British,Male,2026-01-02,E2EHFX{suffix},Engineering,London Office,Permanent,Senior Software Engineer\n";

        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllTextAsync(tempFile, csvContent);

        try
        {
            var login   = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard  = new DataImportWizardPage(_page, _fixture.WebBaseUrl);
            var history = new ImportHistoryPage(_page, _fixture.WebBaseUrl);
            var detail  = new ImportSessionDetailPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

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
}
