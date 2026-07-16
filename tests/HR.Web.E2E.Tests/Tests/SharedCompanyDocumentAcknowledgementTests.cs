using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the employee acknowledgement ACTION itself — checking the confirmation checkbox and
/// clicking "Confirm Acknowledgement" on SharedCompanyDocumentAcknowledgement.razor — which
/// SharedDocumentArchiveTests/CompanyDocumentsTabTests/SharedDocumentVersionHistoryTests never
/// exercise (they only cover the HR-side publish/archive/version flows and the employee-facing
/// "Company Documents" tab's read-only list). Also covers the My Tasks -> Acknowledge task ->
/// document flow, where the task panel navigates the employee away to the same page rather than
/// completing inline (see SharedCompanyDocumentAcknowledgementTaskPanel.razor).
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) to upload, require
/// acknowledgement on, and publish a document, then switches to Tom Williams
/// (tom.williams@acme.example, employee ID 30000000-...-004) to acknowledge it. A newly uploaded
/// document has no audience rules, which SharedCompanyDocumentAudienceMatcher treats as "all
/// employees", so it's visible to Tom without any explicit audience configuration — matching the
/// same assumption CompanyDocumentsTabTests already relies on.
///
/// Persona switching: LoginPage.SwitchAccountAsync's real (dev-mode) path falls through to
/// navigating to /login and calling LoginAsync again — CompanyDocumentsTabTests already does
/// exactly this mid-test (HR upload/publish, then SwitchAccountAsync to the employee), so this
/// file follows that same established precedent rather than re-implementing the equivalent
/// GoToAsync/LoginAsync pair inline.
/// </summary>
[Collection("E2E")]
public sealed class SharedCompanyDocumentAcknowledgementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string HrEmail  = "laura.bennett@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task Employee_CanAcknowledgeAPublishedDocument_ViaDirectNavigation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var ack   = new SharedCompanyDocumentAcknowledgementPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        var documentId = await UploadAndPublishDocumentAsync(
            title, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        await login.SwitchAccountAsync(TomEmail);

        await ack.GoToAsync(AcmeId, documentId);

        Assert.Equal(title, await ack.GetTitleAsync());
        Assert.False(await ack.IsAcknowledgedAsync(),
            "Expected the document to not yet be acknowledged by Tom");

        await ack.CheckConfirmationAsync();
        await ack.ConfirmAcknowledgementAsync();

        Assert.True(await ack.IsAcknowledgedAsync(),
            "Expected the page to show the 'You acknowledged this document on …' success state");
    }

    [Fact]
    public async Task Employee_CanAcknowledge_ViaMyTasks_AcknowledgeTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);
        var ack      = new SharedCompanyDocumentAcknowledgementPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        await UploadAndPublishDocumentAsync(
            title, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        // Title format confirmed in PublishSharedCompanyDocumentHandler:
        // $"Acknowledge: {document.Title} (v{document.VersionNumber})" — a freshly uploaded
        // document is always version 1.
        var taskTitle = $"Acknowledge: {title} (v1)";

        await login.SwitchAccountAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();
        await profile.ClickTaskAsync(taskTitle);
        await taskView.WaitForLoadedAsync();

        Assert.True(await taskView.HasSharedDocumentAcknowledgementPanelAsync(),
            "Expected the shared document acknowledgement panel for an Acknowledge/Document task");

        await taskView.ClickViewAndAcknowledgeDocumentAsync();
        await ack.WaitForLoadedAsync();

        Assert.False(await ack.IsAcknowledgedAsync(),
            "Expected the document to not yet be acknowledged when arriving from the task");

        await ack.CheckConfirmationAsync();
        await ack.ConfirmAcknowledgementAsync();

        Assert.True(await ack.IsAcknowledgedAsync(),
            "Expected the page to show the 'You acknowledged this document on …' success state");

        // Navigate back to the Tasks tab and confirm the same task now shows as Completed — see
        // TaskList.razor for the "task-status-badge--completed" class this asserts against.
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();

        var row = _page.Locator(".e-row").Filter(new() { HasText = taskTitle }).First;
        var badge = row.Locator(".task-status-badge");
        await badge.WaitForAsync(new() { Timeout = 15_000 });

        var badgeClass = await badge.GetAttributeAsync("class") ?? "";
        Assert.Contains("task-status-badge--completed", badgeClass);
        Assert.Equal("Completed", (await badge.InnerTextAsync()).Trim());
    }

    /// <summary>
    /// Uploads a shared document from the Shared Documents list page as HR (same flow as
    /// SharedDocumentArchiveTests / CompanyDocumentsTabTests), turns on acknowledgement with the
    /// given due date, then publishes it. Assumes the caller is already logged in as an
    /// HrAdministrator.
    /// </summary>
    private async Task<Guid> UploadAndPublishDocumentAsync(string title, DateOnly acknowledgementDueDate)
    {
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
            await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

            await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

            var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            await dialog.GetByPlaceholder("Document title").FillAsync(title);

            var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
            await categoryGroup.Locator("span[role='combobox']").First.ClickAsync();
            await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
            await _page.Locator(".e-popup.e-ddl .e-list-item")
                .Filter(new() { HasText = "Policy" })
                .First
                .ClickAsync();

            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await dialog.Locator("input[type='file']").SetInputFilesAsync(tempFile);

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

            await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });

            var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
            Assert.NotNull(href);
            var documentId = Guid.Parse(href!.Split('/').Last());

            await detail.GoToAsync(AcmeId, documentId);
            await detail.RequireAcknowledgementAsync(acknowledgementDueDate);
            await detail.PublishAsync();

            return documentId;
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
