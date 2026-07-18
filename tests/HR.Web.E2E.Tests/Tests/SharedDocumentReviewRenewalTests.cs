using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Keep Previous Versions" extension to CompleteSharedCompanyDocumentReviewDialog.razor:
/// the optional "Renewed File (optional)" InputFile added alongside the existing required Review
/// Notes field, the "Requires employees to acknowledge this version again" checkbox that only
/// renders once both the document requires acknowledgement AND a file has been selected, and the
/// resulting behavior when a file *is* attached — the dialog calls
/// DocumentService.UploadSharedCompanyDocumentVersionAsync (the same call the pre-existing
/// "Upload New Version" dialog makes, with a version note of
/// "Renewed via document review. {reviewNotes}") before calling complete-review, so the Version
/// History grid gains a new row rather than the prior version being silently replaced.
///
/// The notes-only path (no file selected — the dialog's pre-existing behavior, unchanged by this
/// story) and the dialog's read-only metadata / blank-notes validation / Cancel behavior are
/// already covered by SharedDocumentCompleteReviewTests and are not duplicated here. The
/// Version History grid itself (columns, per-version Publication Status, Download links, and the
/// "current vs superseded" row transition after "Upload New Version") is already covered by
/// SharedDocumentVersionHistoryTests — this file only asserts that completing a review with a
/// renewed file drives that same grid the same way.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests. Creates its own document per test
/// (rather than reusing seeded company documents) to avoid mutating shared seed data, matching
/// the convention already used by the other Shared Documents E2E test files.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentReviewRenewalTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";
    private const string PolicyCategory = "Policy";

    [Fact]
    public async Task CompleteReview_WithRenewedFile_UpdatesFooterAndAddsNewVersionHistoryRow()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title         = $"Test Policy {Guid.NewGuid():N}";
        var originalFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-v1-{Guid.NewGuid():N}.pdf");
        var renewedFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-renewed-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, originalFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(1, await detail.WaitForVersionRowCountAsync(1));

            await File.WriteAllBytesAsync(renewedFile, BuildTestPdf());

            var reviewNotes = $"Reviewed, superseded with renewed copy {Guid.NewGuid():N}";
            await detail.CompleteReviewWithRenewedFileAsync(reviewNotes, renewedFile);

            Assert.False(await detail.IsReviewDialogOpenAsync(),
                "Expected the Complete Review dialog to close after a successful submission with a renewed file");

            // A real backend integration test (CompleteReview_AfterUploadingRenewedVersion_
            // StillPersists_LastReviewedFields) proves the server genuinely persists
            // LastReviewedAt/LastReviewedByEmployeeId correctly after this exact chained
            // upload-version + complete-review sequence — so a footer that doesn't show "Last
            // reviewed by" here is a client-side rendering/timing gap, not a backend bug.
            // "Dialog closed" is an unreliable proxy for "the page's post-close reload
            // (SharedDocumentDetail.razor's OnReviewCompletedAsync re-fetching _detail) has landed
            // in the DOM" — the two can arrive in separate render batches. Assert directly on the
            // footer's own content with Playwright's auto-retrying Expect instead of a one-shot
            // read, so the assertion itself waits out that gap rather than a proxy signal.
            var expectedReviewedOn = DateTime.Today.ToString("d MMM yyyy");
            await Assertions.Expect(detail.FooterSummary)
                .ToContainTextAsync($"Last reviewed by Laura Bennett on {expectedReviewedOn}", new() { Timeout = 15_000 });

            // The renewed file must have gone through the version-retention mechanism (a second,
            // additive row) rather than silently replacing the first version in place.
            Assert.Equal(2, await detail.WaitForVersionRowCountAsync(2));

            var originalFileNameFragment = Path.GetFileName(originalFile);
            var renewedFileNameFragment  = Path.GetFileName(renewedFile);

            Assert.Equal("1", await detail.GetVersionRowCellAsync(originalFileNameFragment, 0));
            Assert.Equal("Superseded", await detail.GetVersionRowCellAsync(originalFileNameFragment, 1));

            Assert.Equal("2", await detail.GetVersionRowCellAsync(renewedFileNameFragment, 0));
            Assert.Equal("Draft", await detail.GetVersionRowCellAsync(renewedFileNameFragment, 1));

            // CompleteSharedCompanyDocumentReviewDialog.razor prefixes the version note with
            // "Renewed via document review. " ahead of the operator's own review notes.
            var renewedVersionNote = await detail.GetVersionRowCellAsync(renewedFileNameFragment, 3);
            Assert.Contains("Renewed via document review.", renewedVersionNote);
            Assert.Contains(reviewNotes, renewedVersionNote);
        }
        finally
        {
            if (File.Exists(originalFile)) File.Delete(originalFile);
            if (File.Exists(renewedFile)) File.Delete(renewedFile);
        }
    }

    [Fact]
    public async Task ReviewDialog_ReacknowledgementCheckbox_OnlyAppearsOnceAcknowledgementRequiredAndFileSelected()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title      = $"Test Policy {Guid.NewGuid():N}";
        var originalFile = Path.Combine(Path.GetTempPath(), $"shared-doc-v1-{Guid.NewGuid():N}.pdf");
        var renewedFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-renewed-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, originalFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            // Newly uploaded documents don't require acknowledgement by default (Model.RequiresAcknowledgement
            // defaults to false on UploadSharedCompanyDocumentDialog.razor) — set it explicitly via the
            // Acknowledgement card's Edit dialog, same helper CompanyDocumentsTabTests uses.
            await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.Today.AddMonths(1)));

            await detail.OpenReviewDialogAsync();

            // RequiresAcknowledgement is now true, but no file has been selected yet — the
            // checkbox must stay hidden.
            Assert.False(await detail.IsReviewReacknowledgementCheckboxVisibleAsync(),
                "Expected the reacknowledgement checkbox to stay hidden until a Renewed File is selected");

            await File.WriteAllBytesAsync(renewedFile, BuildTestPdf());
            await detail.SetReviewRenewedFileAsync(renewedFile);

            // Both conditions are now met — the checkbox must appear.
            Assert.True(await detail.IsReviewReacknowledgementCheckboxVisibleAsync(),
                "Expected the reacknowledgement checkbox to appear once RequiresAcknowledgement is true and a file is selected");

            // Don't submit — this test is scoped to the checkbox's visibility, not the
            // acknowledgement-task-creation consequence of checking it (already covered elsewhere
            // for the general Upload New Version flow).
            await detail.ClickReviewCancelAsync();
        }
        finally
        {
            if (File.Exists(originalFile)) File.Delete(originalFile);
            if (File.Exists(renewedFile)) File.Delete(renewedFile);
        }
    }

    [Fact]
    public async Task ReviewDialog_ReacknowledgementCheckbox_StaysHidden_WhenDocumentDoesNotRequireAcknowledgement()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title      = $"Test Policy {Guid.NewGuid():N}";
        var originalFile = Path.Combine(Path.GetTempPath(), $"shared-doc-v1-{Guid.NewGuid():N}.pdf");
        var renewedFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-renewed-{Guid.NewGuid():N}.pdf");
        try
        {
            // No RequireAcknowledgementAsync call here — this document never requires
            // acknowledgement, so the checkbox must stay hidden even once a file is selected.
            await UploadDocumentAsync(title, originalFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            await detail.OpenReviewDialogAsync();

            await File.WriteAllBytesAsync(renewedFile, BuildTestPdf());
            await detail.SetReviewRenewedFileAsync(renewedFile);

            Assert.False(await detail.IsReviewReacknowledgementCheckboxVisibleAsync(),
                "Expected the reacknowledgement checkbox to stay hidden for a document that doesn't require acknowledgement, even with a file selected");

            await detail.ClickReviewCancelAsync();
        }
        finally
        {
            if (File.Exists(originalFile)) File.Delete(originalFile);
            if (File.Exists(renewedFile)) File.Delete(renewedFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentCompleteReviewTests / SharedDocumentVersionHistoryTests), without a Review
    // Frequency or Review Owner since this file's assertions don't depend on either.
    private async Task UploadDocumentAsync(string title, string filePath)
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
            .Filter(new() { HasText = PolicyCategory })
            .First
            .ClickAsync();

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. SharedDocumentArchiveTests).
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
