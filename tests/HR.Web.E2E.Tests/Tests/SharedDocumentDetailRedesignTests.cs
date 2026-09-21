using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the SharedDocumentDetail.razor usability redesign that moved Archive/Mark Expired/Audit
/// History into a "More actions" overflow menu, renamed the page's "Edit" buttons to
/// "Edit details"/"Edit audience"/"Edit acknowledgement settings", split Version History/Review
/// History into a shared SfTab, and moved a version row's Note/Required Ack/Effective Date into a
/// per-row "Details" popup.
///
/// Presentation-only change (no business-rule changes) — the underlying flows themselves (Archive
/// reason validation, Mark Expired confirmation wording, Audit History grid contents, edit-dialog
/// field behaviour, Version History row data) are already covered by SharedDocumentArchiveTests,
/// SharedDocumentExpireTests, SharedCompanyDocumentAcknowledgementSettingsTests,
/// SharedDocumentMetadataEditTests, SharedDocumentAudienceTests, and SharedDocumentVersionHistoryTests
/// respectively — this file is scoped narrowly to the new UI surface itself: the dropdown menu's
/// contents/behaviour, the renamed buttons opening the correct dialogs, and the version-detail
/// popup's own contents.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E test files.
/// </summary>
public sealed class SharedDocumentDetailRedesignTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task MoreActionsMenu_ForDraftDocument_ListsArchiveMarkExpiredAndAuditHistory()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            // A freshly uploaded (Draft) document offers all three actions.
            Assert.True(await detail.IsArchiveButtonVisibleAsync(),
                "Expected 'Archive' to be listed in the More actions menu for a Draft document");
            Assert.True(await detail.IsExpireButtonVisibleAsync(),
                "Expected 'Mark Expired' to be listed in the More actions menu for a Draft document");

            // Audit History is opened directly via its own helper — exercising it here proves the
            // menu item itself is clickable and drives the dialog open, not just present.
            await detail.OpenAuditHistoryDialogAsync();
            Assert.True(await detail.IsAuditHistoryDialogOpenAsync(),
                "Expected the Audit History dialog to open from the More actions menu");
            await detail.CloseAuditHistoryDialogAsync();
            Assert.False(await detail.IsAuditHistoryDialogOpenAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MoreActionsMenu_ForArchivedDocument_OnlyListsAuditHistory()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            await detail.ArchiveAsync("No longer applicable");
            Assert.Equal("Archived", await detail.GetStatusAsync());

            // Archive/Mark Expired are gone once already Archived (BuildMoreActionsItems only adds
            // them while Status is Draft or Published) — Audit History always remains.
            Assert.False(await detail.IsArchiveButtonVisibleAsync(),
                "Expected 'Archive' to be gone from the More actions menu for an Archived document");
            Assert.False(await detail.IsExpireButtonVisibleAsync(),
                "Expected 'Mark Expired' to be gone from the More actions menu for an Archived document");

            await detail.OpenAuditHistoryDialogAsync();
            Assert.True(await detail.IsAuditHistoryDialogOpenAsync(),
                "Expected 'Audit History' to remain available in the More actions menu for an Archived document");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RenamedEditButtons_EachOpenTheirOwnDialog()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            // "Edit details" (header) -> EditSharedCompanyDocumentMetadataDialog.razor.
            await detail.OpenMetadataDialogAsync();
            Assert.True(await detail.IsMetadataDialogOpenAsync(),
                "Expected 'Edit details' to open the Edit Document Metadata dialog");
            await _page.Keyboard.PressAsync("Escape");
            await detail.WaitForOverlayToClearAsync();

            // "Edit audience" (Audience card) -> EditSharedCompanyDocumentAudienceDialog.razor.
            await detail.OpenAudienceDialogAsync();
            Assert.True(await detail.IsAudienceDialogOpenAsync(),
                "Expected 'Edit audience' to open the Edit Document Audience dialog");
            await _page.Keyboard.PressAsync("Escape");
            await detail.WaitForOverlayToClearAsync();

            // "Edit acknowledgement settings" (Acknowledgement card) -> EditSharedCompanyDocumentAcknowledgementDialog.razor.
            await detail.OpenEditAcknowledgementDialogAsync();
            Assert.True(await detail.IsAcknowledgementDialogOpenAsync(),
                "Expected 'Edit acknowledgement settings' to open the Edit Acknowledgement Settings dialog");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VersionDetailPopup_ShowsNoteRequiredAckAndEffectiveDate_ForAVersionRow()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, effectiveDateDdMmYyyy: "15/03/2026");

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(1, await detail.WaitForVersionRowCountAsync(1));

            var fileNameFragment = Path.GetFileName(tempFile);
            var (note, requiredAck, effectiveDate) = await detail.GetVersionDetailAsync(fileNameFragment);

            // v1's own upload dialog leaves the version note blank, so the popup shows the
            // em-dash placeholder (SharedDocumentDetail.razor's "row.VersionNote is blank -> '—'").
            Assert.Equal("—", note);
            Assert.Equal("No", requiredAck);
            Assert.Contains("2026", effectiveDate);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentVersionHistoryTests / SharedDocumentArchiveTests) and leaves the browser on
    // that list, with the new title visible in the grid so its row's href can be read to discover
    // the generated document id.
    private async Task UploadDocumentAsync(string title, string filePath, string? effectiveDateDdMmYyyy = null)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        if (effectiveDateDdMmYyyy is not null)
        {
            var effectiveDateInput = dialog.Locator(".e-date-wrapper input.e-input").First;
            await effectiveDateInput.ClickAsync();
            await effectiveDateInput.FillAsync(effectiveDateDdMmYyyy);
            await _page.Keyboard.PressAsync("Tab");
        }

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. SharedDocumentVersionHistoryTests).
    private async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        return Guid.Parse(href.Split('/').Last());
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
