using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Archive" action on SharedDocumentDetail.razor: the header Archive button (hidden
/// once a document is already Archived), its confirmation dialog's required-reason validation,
/// and the resulting Archived state (Status badge, Archive button disappearing, and the
/// "Archived by … — reason: …" segment on the page's footer summary line). Exercises the flow
/// from both Draft and Published starting states, since archiving is allowed from either.
///
/// Upload (and, for the second test, Publish) here follow the same UI flows already covered in
/// SharedDocumentUploadTests / SharedDocumentVersionHistoryTests — this file does not re-assert
/// upload-dialog field validation or the Version History grid, only what Archive adds on top.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentArchiveTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ArchiveDraftDocument_ValidatesReason_ThenArchives()
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

            Assert.Equal("Draft", await detail.GetStatusAsync());
            Assert.True(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to be visible for a Draft document");

            // Clicking Archive with an empty reason must show the inline validation message and
            // keep the dialog open, without calling the backend.
            await detail.OpenArchiveDialogAsync();
            await detail.ClickArchiveConfirmAsync();

            Assert.Equal("A reason is required.", await detail.GetArchiveErrorAsync());
            Assert.True(await detail.IsArchiveDialogOpenAsync(),
                "Expected the Archive dialog to stay open when the reason is blank");
            Assert.Equal("Draft", await detail.GetStatusAsync());

            // Now supply a reason and confirm.
            var reason = "No longer applicable";
            await detail.FillArchiveReasonAsync(reason);
            await detail.ClickArchiveConfirmAsync();
            await detail.WaitForArchiveDialogToCloseAsync();

            Assert.Equal("Archived", await detail.GetStatusAsync());
            Assert.False(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to disappear once the document is Archived");

            var footerSummary = await detail.GetFooterSummaryTextAsync();
            Assert.Contains("Archived by", footerSummary);
            Assert.Contains($"reason: {reason}", footerSummary);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ArchivePublishedDocument_ShowsArchivedState()
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

            await detail.PublishAsync();
            Assert.Equal("Published", await detail.GetStatusAsync());
            // Publish is only offered while Draft, so it must be gone now — but Archive stays
            // available (archiving is allowed from both Draft and Published).
            Assert.False(await detail.IsPublishButtonVisibleAsync());
            Assert.True(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to remain visible for a Published document");

            var reason = "Superseded by updated policy";
            await detail.ArchiveAsync(reason);

            Assert.Equal("Archived", await detail.GetStatusAsync());
            Assert.False(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to disappear once the document is Archived");

            var footerSummary = await detail.GetFooterSummaryTextAsync();
            Assert.Contains("Archived by", footerSummary);
            Assert.Contains($"reason: {reason}", footerSummary);
            // The Published-state summary segment should still be present alongside Archived.
            Assert.Contains("Published by", footerSummary);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentVersionHistoryTests) and leaves the browser on
    // that list, with the new title visible in the grid so its row's href can be read to
    // discover the generated document id.
    private async Task UploadDocumentAsync(string title, string filePath)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        // Select a category via the shared Syncfusion SfDropDownList helper.
        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. EmploymentTypeEditCloseBehaviorTests
    // and SharedDocumentVersionHistoryTests).
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
