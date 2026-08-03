using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the admin "Upload Document" dialog on the employee Documents tab (UploadDocumentDialog.
/// razor, EmployeeSelfUpload="false") is split into two tabs — "Document Details" (Title/Document
/// Type/Description/Issue Date/Expiry Date) and "File" (the file input) — rather than a single flat
/// form. Distinct from the self-service "Upload {DocumentType}" dialog covered by
/// MyProfilePage.UploadRequestedDocumentAsync, and from the "Upload Document" dialog for shared
/// company documents (UploadSharedCompanyDocumentDialog.razor, no tabs) covered by
/// CompanyDocumentsTabTests — those are separate, unrelated dialogs sharing a similar name.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeAdminUploadDocumentDialogTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task UploadDocumentDialog_IsSplitIntoDocumentDetailsAndFileTabs()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        await empAdmin.OpenUploadDocumentDialogAsync();

        Assert.True(await empAdmin.HasUploadDialogDocumentDetailsTabAsync(),
            "Expected a 'Document Details' tab in the Upload Document dialog");
        Assert.True(await empAdmin.HasUploadDialogFileTabAsync(),
            "Expected a 'File' tab in the Upload Document dialog");

        // The file input lives on the separate "File" tab, not alongside the Document Details
        // fields on first open.
        Assert.False(await empAdmin.IsUploadDialogFileInputVisibleAsync(),
            "Did not expect the file input to be visible before switching to the 'File' tab");
    }

    [Fact]
    public async Task UploadDocumentDialog_CompletesAcrossBothTabs_AndAppearsInGrid()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        var title = $"E2E Upload {Guid.NewGuid():N}"[..30];
        var tempFile = Path.Combine(Path.GetTempPath(), $"admin-upload-{Guid.NewGuid():N}.pdf");

        try
        {
            await empAdmin.OpenUploadDocumentDialogAsync();

            // Document Details tab.
            await empAdmin.FillUploadDialogTitleAsync(title);
            await empAdmin.SelectUploadDialogDocumentTypeAsync("Passport");

            // File tab.
            await File.WriteAllBytesAsync(tempFile, [0x25, 0x50, 0x44, 0x46, 0x2D, 0x00, 0x00, 0x00]);
            await empAdmin.SelectUploadDialogFileAsync(tempFile);

            await empAdmin.SubmitUploadDocumentDialogAsync();

            Assert.True(await empAdmin.HasDocumentAsync(title),
                "Expected the newly uploaded document to appear in the Documents grid");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
