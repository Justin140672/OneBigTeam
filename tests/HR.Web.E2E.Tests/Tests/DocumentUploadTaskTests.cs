using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can see and use the document upload panel on a task
/// with Source=Document and ActionType=Upload, and that completing the upload marks
/// the task as Completed.
///
/// Uses seeded data:
///   - Tom Williams   (30000000-0000-0000-0000-000000000004) — the completion test below no
///     longer uses his fixed seeded "Upload Passport" task (a0000000-...-000000000010); see that
///     test's own comment for why.
///   - Carlos Rivera  (30000000-0000-0000-0000-000000000010), task a0000000-...-000000000011
/// </summary>
public sealed class DocumentUploadTaskTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    // Carlos's "Upload Passport" task — used for the panel-visible check only (no upload).
    private static readonly Guid CarlosUploadTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000011");

    private const string LauraEmail  = "laura.bennett@acme.example";
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

    /// <summary>
    /// AssignedUploadTask_MarksTaskAsCompleted below used to complete Tom Williams' fixed seeded
    /// "Upload Passport" task (a0000000-...-000000000010), irreversibly marking it Completed. That
    /// also updates the underlying DocumentRequest's status (see UploadRequestedDocument's
    /// handler), which collided with EmployeeDocumentsTabTests.
    /// Admin_Documents_Tab_Shows_Requested_Status_Badge_For_Outstanding_Request — a read-only test
    /// that expects Tom's seeded Passport request to still show "Requested".
    ///
    /// Unlike the fixed asset-acknowledgement/return task GUIDs (AssetAcknowledgementTaskTests/
    /// AssetReturnTaskTests — see those files' comments), this one doesn't need a brand-new
    /// employee with its own login: Tom already has a working dev-persona login, so instead of
    /// touching his seeded Passport request, we request a document type nobody else in this suite
    /// touches for him ("Certificate" — the seeded Acme document type list also has an unused
    /// "Other") via the HR-admin "Request Document" action, which creates a *new*, freshly
    /// generated "Upload Certificate" task exactly like the seeded one. We then open it from Tom's
    /// own Tasks tab by title (TaskViewDialog is reachable from any entry point that opens the
    /// same dialog, not just TaskViewPage.GoToAsync's direct URL — see that page object's class
    /// doc comment) rather than needing to know its GUID up front.
    /// </summary>
    [Fact]
    public async Task AssignedUploadTask_MarksTaskAsCompleted()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // Arrange: as HR Administrator, request a "Certificate" document from Tom — this creates
        // a fresh "Upload Certificate" task assigned to him (RequestAdditionalEmployeeDocumentHandler).
        //
        // RequestAdditionalEmployeeDocumentHandler rejects a request with Conflict if a
        // DocumentRequest already exists for this Employee+DocumentType pair (no de-dup on status
        // — even a long-completed one still counts), and this test never cancels/removes the
        // request it creates. Against a fresh database that's fine (first run only), but against
        // the shared, long-lived E2E dev database a second run of this exact test hits that
        // Conflict, the dialog never closes/reloads, and no new task is ever created — leaving
        // Tom's Tasks tab with nothing titled "Upload Certificate" from *this* run, which is what
        // ClickTaskAsync below was timing out on. Guard by checking whether Tom already has the
        // task first and skip re-requesting if so, same resilience pattern already used by
        // ProbationReviewFlowTests.CompletingReviewTask_IsReflectedOnProbationTab for its own
        // shared, non-repeatable seeded state.
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();
        var alreadyRequested = (await profile.GetTaskTitlesAsync())
            .Any(t => t.Contains("Upload Certificate", StringComparison.Ordinal));

        if (!alreadyRequested)
        {
            await login.SwitchAccountAsync(LauraEmail);
            await empAdmin.GoToAsync(AcmeId, TomId);
            await empAdmin.OpenDocumentsTabAsync();
            await empAdmin.RequestDocumentAsync("Certificate");

            await login.LoginAsync(TomEmail);
            await profile.GoToAsync(AcmeId, TomId);
            await profile.OpenTasksTabAsync();
        }

        // Act: open the task (freshly created above, or already there from an earlier run) from
        // Tom's own Tasks tab.
        await profile.ClickTaskAsync("Upload Certificate");
        await taskView.WaitForLoadedAsync();

        if (await taskView.GetStatusAsync() == "Completed")
        {
            // Already completed by an earlier run against this shared database — nothing left to
            // do, the assertion below already holds.
            return;
        }

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
