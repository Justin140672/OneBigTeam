using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Shared Company Document acknowledgement-settings feature set added alongside the
/// audit history dialog:
/// - The standalone HR Settings page's "Default Acknowledgement Statement" field
///   (HrSettingsPage.razor — moved off the Company Settings tab, see class doc history).
/// - UploadSharedCompanyDocumentDialog.razor's auto-populate-from-default, live preview, and
///   required-when-enabled validation for the Acknowledgement Statement field.
/// - EditSharedCompanyDocumentAcknowledgementDialog.razor's Draft-editable / Published-locked
///   behaviour and its "Reset to Default" button.
/// - SharedCompanyDocumentAuditHistoryDialog.razor, opened from the document detail page's header.
///
/// Complements SharedDocumentUploadTests (basic upload flow), CompanyDocumentsTabTests (uses the
/// same upload-then-publish helper pattern), and HrSettingsPageTests (HR Settings page
/// conventions) — this file focuses specifically on the acknowledgement-statement and
/// audit-history additions.
///
/// Uses Laura Bennett (HrAdministrator) for both the HR Settings page (hr-settings:manage /
/// Session.IsHrAdministrator) and the document upload/publish/edit flows
/// (shared-document:manage) — the "Default Acknowledgement Statement" field used to live on
/// Company Settings (CompanyAdministrator-only) but has since moved to the standalone HR Settings
/// page, which only an HrAdministrator can reach.
/// </summary>
[Collection("E2E")]
public sealed class SharedCompanyDocumentAcknowledgementSettingsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompanySettings_UpdateDefaultAcknowledgementStatement_PersistsAfterReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await hrSettings.GoToAsync(AcmeId);

        var statement = $"Default acknowledgement statement {Guid.NewGuid():N}";
        await hrSettings.SetDefaultAcknowledgementStatementAsync(statement);

        await hrSettings.SaveAsync();
        Assert.False(await hrSettings.HasErrorAsync(),
            "Expected no error after saving the default acknowledgement statement");

        // Reload the page for real (re-navigate) to exercise the settings-hydration path, not
        // just in-memory Blazor state — same pattern as HrSettingsPageTests.
        await hrSettings.GoToAsync(AcmeId);

        Assert.Equal(statement, await hrSettings.GetDefaultAcknowledgementStatementAsync());
    }

    [Fact]
    public async Task UploadDialog_RequiresAcknowledgement_AutoPopulatesFromCompanyDefault_AndShowsLivePreview()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var upload = new UploadSharedCompanyDocumentDialogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var defaultStatement = $"Default statement {Guid.NewGuid():N}";
        await hrSettings.GoToAsync(AcmeId);
        await hrSettings.SetDefaultAcknowledgementStatementAsync(defaultStatement);
        await hrSettings.SaveAsync();
        Assert.False(await hrSettings.HasErrorAsync());

        await upload.GoToListAsync(AcmeId);
        await upload.OpenAsync();

        await upload.FillTitleAsync($"Test Policy {Guid.NewGuid():N}");
        await upload.SelectCategoryAsync("Policy");

        await upload.CheckRequiresAcknowledgementAsync();

        Assert.Equal(defaultStatement, await upload.GetAcknowledgementStatementValueAsync());
        Assert.Equal(defaultStatement, await upload.GetAcknowledgementPreviewTextAsync());
    }

    [Fact]
    public async Task UploadDialog_BlocksSave_WhenAcknowledgementStatementClearedWhileRequired()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var upload = new UploadSharedCompanyDocumentDialogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await upload.GoToListAsync(AcmeId);
        await upload.OpenAsync();

        await upload.FillTitleAsync($"Test Policy {Guid.NewGuid():N}");
        await upload.SelectCategoryAsync("Policy");

        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await upload.SetFileAsync(tempFile);

            await upload.CheckRequiresAcknowledgementAsync();
            await upload.FillAcknowledgementStatementAsync("");

            await upload.ClickUploadAsync();

            // ValidateExtra runs client-side and keeps the dialog open (returns the error to
            // GlobalError) rather than posting to the server — same "blocked, not closed" pattern
            // as SharedDocumentDetailPage's Archive/Review-notes validation paths.
            Assert.True(await upload.IsOpenAsync(),
                "Expected the dialog to remain open when the acknowledgement statement is blank while required");

            var error = await upload.GetGlobalErrorAsync();
            Assert.NotNull(error);
            Assert.Contains("acknowledgement statement is required", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Draft_HR_CanFreelyEditAcknowledgementStatement()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var documentId = await UploadDraftDocumentAsync();

        await detail.GoToAsync(AcmeId, documentId);
        Assert.Equal("Draft", await detail.GetStatusAsync());

        await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        await detail.OpenEditAcknowledgementDialogAsync();
        Assert.False(await detail.IsAcknowledgementStatementReadOnlyAsync(),
            "Expected the statement field to be editable while the document is still Draft");

        var newStatement = $"Updated statement {Guid.NewGuid():N}";
        await detail.FillAcknowledgementStatementAsync(newStatement);
        await detail.SaveEditAcknowledgementDialogAsync();

        await detail.OpenEditAcknowledgementDialogAsync();
        Assert.Equal(newStatement, await detail.GetAcknowledgementStatementValueAsync());
    }

    [Fact]
    public async Task Published_AcknowledgementStatementField_IsLockedWithExplanatoryNote()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var documentId = await UploadDraftDocumentAsync();

        await detail.GoToAsync(AcmeId, documentId);
        await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));
        await detail.PublishAsync();
        Assert.Equal("Published", await detail.GetStatusAsync());

        await detail.OpenEditAcknowledgementDialogAsync();

        Assert.True(await detail.IsAcknowledgementStatementReadOnlyAsync(),
            "Expected the statement field to be read-only once the document is Published");
        Assert.True(await detail.IsAcknowledgementLockedNoteVisibleAsync(),
            "Expected the 'locked after publishing' explanatory note to be visible");
        Assert.True(await detail.IsResetAcknowledgementStatementButtonDisabledAsync(),
            "Expected the 'Reset to Default' button to be disabled once the field is locked");
    }

    [Fact]
    public async Task EditDialog_ResetToDefault_RestoresCompanyDefaultStatement()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var defaultStatement = $"Default statement {Guid.NewGuid():N}";
        await hrSettings.GoToAsync(AcmeId);
        await hrSettings.SetDefaultAcknowledgementStatementAsync(defaultStatement);
        await hrSettings.SaveAsync();
        Assert.False(await hrSettings.HasErrorAsync());

        var documentId = await UploadDraftDocumentAsync();

        await detail.GoToAsync(AcmeId, documentId);
        await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        // Set the statement to something other than the default first, so the reset is a
        // meaningful assertion rather than a no-op.
        await detail.OpenEditAcknowledgementDialogAsync();
        await detail.FillAcknowledgementStatementAsync($"Custom statement {Guid.NewGuid():N}");
        await detail.SaveEditAcknowledgementDialogAsync();

        await detail.OpenEditAcknowledgementDialogAsync();
        await detail.ClickResetAcknowledgementStatementToDefaultAsync();

        Assert.Equal(defaultStatement, await detail.GetAcknowledgementStatementValueAsync());
    }

    [Fact]
    public async Task AuditHistoryDialog_OpensFromDetailPage_AndShowsEntry_AfterPublish()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        var documentId = await UploadDraftDocumentAsync(title);

        await detail.GoToAsync(AcmeId, documentId);
        await detail.PublishAsync();

        await detail.OpenAuditHistoryDialogAsync();
        Assert.True(await detail.IsAuditHistoryDialogOpenAsync());

        var rowCount = await detail.GetAuditHistoryRowCountAsync();
        Assert.True(rowCount >= 1,
            $"Expected at least one audit history entry after uploading and publishing, got {rowCount}");

        // Both the "created" and "published" events reference the document's title
        // (SharedCompanyDocumentPublishedAuditEvent.Summary = "Document '{Title}' published") —
        // filtering the row by the unique title, rather than the action text itself, keeps this
        // resilient to that exact wording.
        await detail.ClickViewAuditHistoryRowAsync(title);
        Assert.True(await detail.IsAuditDetailDialogOpenAsync());

        var dialogText = await detail.GetAuditDetailDialogTextAsync();
        Assert.Contains(title, dialogText);

        await detail.CloseAuditDetailDialogAsync();
        Assert.False(await detail.IsAuditDetailDialogOpenAsync());

        await detail.CloseAuditHistoryDialogAsync();
        Assert.False(await detail.IsAuditHistoryDialogOpenAsync());
    }

    /// <summary>
    /// Uploads a shared document via the list page's "Upload Document" dialog (no acknowledgement
    /// requirement, no publish) and returns its Id. Assumes the caller is already logged in as an
    /// HrAdministrator. Same upload mechanics as CompanyDocumentsTabTests.UploadAndPublishDocumentAsync,
    /// but stops after upload rather than also publishing, since several tests here need to drive
    /// the acknowledgement-settings dialog on a still-Draft document first.
    /// </summary>
    private async Task<Guid> UploadDraftDocumentAsync(string? title = null)
    {
        title ??= $"Test Policy {Guid.NewGuid():N}";
        var upload = new UploadSharedCompanyDocumentDialogPage(_page, _fixture.WebBaseUrl);
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await upload.GoToListAsync(AcmeId);
            await upload.OpenAsync();

            await upload.FillTitleAsync(title);
            await upload.SelectCategoryAsync("Policy");

            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await upload.SetFileAsync(tempFile);

            await upload.UploadAndWaitForCloseAsync();

            return await upload.GetUploadedDocumentIdAsync(title);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // %PDF- followed by padding, so magic-byte content validation passes.
    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
