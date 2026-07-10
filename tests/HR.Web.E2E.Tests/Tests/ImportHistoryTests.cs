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

        var csvContent =
            "First Name,Last Name,Work Email,Start Date,Employee Number\n" +
            $"History,Employee,{workEmail},2026-01-01,{employeeNumber}\n";

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

            await history.OpenSessionAsync(fileName);

            Assert.Contains("/data-import/sessions/", _page.Url);

            var detailStatus = await detail.GetStatusAsync();
            Assert.Equal("Imported", detailStatus);

            var pageText = await _page.Locator("body").InnerTextAsync();
            Assert.Contains("Total Rows", pageText);
            Assert.Contains("Successful Rows", pageText);
            Assert.Contains("Failed Rows", pageText);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
