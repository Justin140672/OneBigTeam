using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the three shared-company-document edit dialogs
/// hosted by SharedDocumentDetail.razor —
/// EditSharedCompanyDocument{Metadata,Audience,Acknowledgement}Dialog.razor. Each dialog loads the
/// document's Version when it opens, sends it as ExpectedVersion on save, and treats an API 409
/// (code=="concurrency") as a conflict: it renders the shared &lt;SaveConflictBanner&gt; ("Someone
/// else changed this document while you were editing. Your changes have not been saved.") plus a
/// "Reload latest values" button, keeps the dialog open, and preserves the editor's entered
/// values. "Reload latest values" re-fetches the document, repopulates the form at the latest
/// version and clears the banner — after which a re-save succeeds and closes the dialog.
///
/// The "second editor" is a second tab in the same authenticated HR-admin context that changes the
/// same document's metadata (Title) first, bumping its Version and making the first tab's save
/// stale — regardless of which of the three dialogs the first tab has open, since Version is
/// per-document.
///
/// Each test uploads its own uniquely-titled document, so nothing here contends with seeded data
/// or the other parallel Shared Documents test files — deterministic at maxParallelThreads=15.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests.
/// </summary>
public sealed class SharedCompanyDocumentEditConcurrencyConflictTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task MetadataDialog_SaveAfterAnotherActorChangedDocument_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var originalTitle = $"Test Policy {Guid.NewGuid():N}";
        var firstTabTitle = $"First Tab {Guid.NewGuid():N}";
        var otherTabTitle = $"Other Tab {Guid.NewGuid():N}";
        var finalTitle    = $"Final {Guid.NewGuid():N}";
        var tempFile      = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");

        try
        {
            await UploadDocumentAsync(originalTitle, tempFile);
            var documentId = await GetUploadedDocumentIdAsync(originalTitle);

            // ── Tab 1: open the metadata dialog and start editing the Title (loads Version v1) ──
            await detail.GoToAsync(AcmeId, documentId);
            await detail.OpenMetadataDialogAsync();
            await detail.SetMetadataTitleAsync(firstTabTitle);

            // ── Tab 2 (same context / persona): change the same document's Title and save first ──
            var otherPage = await _context.NewPageAsync();
            try
            {
                var otherDetail = new SharedDocumentDetailPage(otherPage, _fixture.WebBaseUrl);
                await otherDetail.GoToAsync(AcmeId, documentId);
                await otherDetail.ChangeMetadataTitleAsync(otherTabTitle);
            }
            finally
            {
                await otherPage.CloseAsync();
            }

            // ── Tab 1: saving now is stale → conflict banner, dialog stays open, input preserved ──
            await detail.SaveMetadataDialogExpectingConflictAsync();

            Assert.True(await detail.IsMetadataConflictBannerVisibleAsync(),
                "Expected the optimistic-concurrency conflict banner after a stale save");
            Assert.True(await detail.IsMetadataDialogOpenAsync(),
                "The Edit Document Metadata dialog should stay open after a concurrency conflict");
            Assert.Equal(firstTabTitle, await detail.GetMetadataTitleValueAsync());

            // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's value ──
            await detail.ClickMetadataReloadLatestAsync();

            Assert.False(await detail.IsMetadataConflictBannerVisibleAsync(),
                "Expected the conflict banner to clear after reloading latest values");
            await detail.WaitForMetadataTitleValueAsync(otherTabTitle);

            // ── Tab 1: re-edit against the fresh version and save successfully ──
            await detail.SetMetadataTitleAsync(finalTitle);
            await detail.SaveMetadataDialogExpectingSuccessAsync();

            Assert.False(await detail.IsMetadataDialogOpenAsync(),
                "Expected the dialog to close after a successful save against the reloaded version");

            await detail.GoToAsync(AcmeId, documentId);
            Assert.Equal(finalTitle, await detail.GetTitleAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AudienceDialog_SaveAfterAnotherActorChangedDocument_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var bumpTitle = $"Bumped {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");

        try
        {
            await UploadDocumentAsync(title, tempFile);
            var documentId = await GetUploadedDocumentIdAsync(title);

            // ── Tab 1: open the Audience dialog (loads Version v1) ──
            await detail.GoToAsync(AcmeId, documentId);
            await detail.OpenAudienceDialogAsync();

            // ── Tab 2: bump the document's version via a metadata Title change ──
            var otherPage = await _context.NewPageAsync();
            try
            {
                var otherDetail = new SharedDocumentDetailPage(otherPage, _fixture.WebBaseUrl);
                await otherDetail.GoToAsync(AcmeId, documentId);
                await otherDetail.ChangeMetadataTitleAsync(bumpTitle);
            }
            finally
            {
                await otherPage.CloseAsync();
            }

            // ── Tab 1: saving the audience dialog now is stale → conflict banner, dialog stays open ──
            await detail.SaveAudienceDialogExpectingConflictAsync();

            Assert.True(await detail.IsAudienceConflictBannerVisibleAsync(),
                "Expected the optimistic-concurrency conflict banner on the Audience dialog after a stale save");
            Assert.True(await detail.IsAudienceDialogOpenAsync(),
                "The Edit Document Audience dialog should stay open after a concurrency conflict");

            // ── Tab 1: reload latest clears the banner, then a re-save succeeds ──
            await detail.ClickAudienceReloadLatestAsync();

            Assert.False(await detail.IsAudienceConflictBannerVisibleAsync(),
                "Expected the Audience dialog's conflict banner to clear after reloading latest values");

            await detail.SaveAudienceDialogExpectingSuccessAsync();

            Assert.False(await detail.IsAudienceDialogOpenAsync(),
                "Expected the Audience dialog to close after a successful save against the reloaded version");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AcknowledgementDialog_SaveAfterAnotherActorChangedDocument_ShowsConflictBanner_ThenReloadRecovers()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title     = $"Test Policy {Guid.NewGuid():N}";
        var bumpTitle  = $"Bumped {Guid.NewGuid():N}";
        var tempFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");

        try
        {
            await UploadDocumentAsync(title, tempFile);
            var documentId = await GetUploadedDocumentIdAsync(title);

            // ── Tab 1: open the Acknowledgement Settings dialog (loads Version v1) ──
            await detail.GoToAsync(AcmeId, documentId);
            await detail.OpenEditAcknowledgementDialogAsync();

            // ── Tab 2: bump the document's version via a metadata Title change ──
            var otherPage = await _context.NewPageAsync();
            try
            {
                var otherDetail = new SharedDocumentDetailPage(otherPage, _fixture.WebBaseUrl);
                await otherDetail.GoToAsync(AcmeId, documentId);
                await otherDetail.ChangeMetadataTitleAsync(bumpTitle);
            }
            finally
            {
                await otherPage.CloseAsync();
            }

            // ── Tab 1: saving the acknowledgement dialog now is stale → conflict banner, dialog stays open ──
            await detail.SaveAcknowledgementDialogExpectingConflictAsync();

            Assert.True(await detail.IsAcknowledgementConflictBannerVisibleAsync(),
                "Expected the optimistic-concurrency conflict banner on the Acknowledgement dialog after a stale save");
            Assert.True(await detail.IsAcknowledgementDialogOpenAsync(),
                "The Edit Acknowledgement Settings dialog should stay open after a concurrency conflict");

            // ── Tab 1: reload latest clears the banner, then a re-save succeeds ──
            await detail.ClickAcknowledgementReloadLatestAsync();

            Assert.False(await detail.IsAcknowledgementConflictBannerVisibleAsync(),
                "Expected the Acknowledgement dialog's conflict banner to clear after reloading latest values");

            await detail.SaveAcknowledgementDialogExpectingSuccessAsync();

            Assert.False(await detail.IsAcknowledgementDialogOpenAsync(),
                "Expected the Acknowledgement dialog to close after a successful save against the reloaded version");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page with the "Policy" category —
    // same flow as SharedDocumentAudienceTests / SharedDocumentMetadataEditTests.
    private async Task UploadDocumentAsync(string title, string filePath)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

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
