using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the employee data import wizard end-to-end flow: upload a CSV, validate it,
/// view the row preview, and confirm the import to create employee records.
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

        var csvContent =
            "First Name,Last Name,Work Email,Start Date,Employee Number\n" +
            $"Imported,Employee,{workEmail},2026-01-01,{employeeNumber}\n";

        var tempFile = Path.Combine(Path.GetTempPath(), $"employee-import-{suffix}.csv");
        await File.WriteAllTextAsync(tempFile, csvContent);

        try
        {
            var login  = new LoginPage(_page, _fixture.WebBaseUrl);
            var wizard = new DataImportWizardPage(_page, _fixture.WebBaseUrl);

            await login.GoToAsync();
            await login.LoginAsync(LauraEmail);

            await wizard.GoToAsync(AcmeId);
            await wizard.UploadFileAsync(tempFile);
            await wizard.ValidateAsync();
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
}
