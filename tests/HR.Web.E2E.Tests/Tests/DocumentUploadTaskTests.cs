using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can see and use the document upload panel on a task
/// with Source=Document and ActionType=Upload, and that completing the upload marks
/// the task as Completed.
///
/// Uses seeded data:
///   - Tom Williams   (30000000-0000-0000-0000-000000000004), task a0000000-...-000000000010
///   - Carlos Rivera  (30000000-0000-0000-0000-000000000010), task a0000000-...-000000000011
/// </summary>
[Collection("E2E")]
public sealed class DocumentUploadTaskTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    // Tom's "Upload Passport" task — used for the completion test (mutates state).
    private static readonly Guid TomUploadTaskId    = Guid.Parse("a0000000-0000-0000-0000-000000000010");

    // Carlos's "Upload Passport" task — used for the panel-visible check only (no upload).
    private static readonly Guid CarlosUploadTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000011");

    private const string TomEmail    = "tom.williams@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";

    [Fact]
    public async Task DocumentUploadTask_ShowsUploadPanel_WhenTaskIsOpen()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CarlosEmail);

        await taskView.GoToAsync(AcmeId, CarlosId, CarlosUploadTaskId);

        Assert.True(await taskView.HasDocumentUploadPanelAsync(),
            "Expected the document upload panel to be shown for an Upload/Document task");

        // Leave and probation panels must NOT appear on a document upload task.
        Assert.False(await taskView.HasLeaveReviewPanelAsync(),
            "Leave review panel should not appear on a document upload task");
        Assert.False(await taskView.HasProbationReviewPanelAsync(),
            "Probation review panel should not appear on a document upload task");
    }

    [Fact]
    public async Task DocumentUploadTask_UploadFile_MarksTaskAsCompleted()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await taskView.GoToAsync(AcmeId, TomId, TomUploadTaskId);

        Assert.True(await taskView.HasDocumentUploadPanelAsync(),
            "Expected the document upload panel to be visible");

        // Write a minimal valid PDF to a temp file for the upload.
        var tempFile = Path.Combine(Path.GetTempPath(), $"passport-{Guid.NewGuid():N}.pdf");
        try
        {
            var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
            var pdf   = new byte[magic.Length + 1020];
            magic.CopyTo(pdf, 0);
            await File.WriteAllBytesAsync(tempFile, pdf);

            await taskView.AttachUploadFileAsync(tempFile);
            await taskView.SubmitDocumentUploadAsync();

            Assert.Equal("Completed", await taskView.GetStatusAsync());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
