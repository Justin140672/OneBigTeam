using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the read-only "Review History" grid on SharedDocumentDetail.razor (Review Date,
/// Reviewer, Notes, Previous Review Date columns, populated automatically whenever a review is
/// completed) — not to be confused with SharedDocumentCompleteReviewTests, which covers the
/// "Review Document" button / CompleteSharedCompanyDocumentReviewDialog.razor flow itself (notes
/// validation, footer "Last reviewed by …" text, Next Review Date advancing). This file reuses
/// that flow purely as arrangement (via SharedDocumentDetailPage.CompleteReviewAsync) to get rows
/// into the grid, and does not re-assert the dialog's own behavior.
///
/// Also distinct from SharedDocumentVersionHistoryTests, which covers the "Version History" grid
/// immediately above this one on the same page — the two grids share the HrGrid component and
/// several CSS classes (".e-grid"/".e-row"/".e-headercell"), so every helper added to
/// SharedDocumentDetailPage for this grid is deliberately scoped to the "Review History"
/// overview-card to avoid colliding with the Version History grid's own rows/headers.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E test files. Completing a review as this HR
/// Administrator always attributes the review to Laura Bennett (per
/// SharedDocumentCompleteReviewTests' own "Last reviewed by Laura Bennett" assertion), so the
/// Reviewer column is asserted against that same fixed name throughout.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentReviewHistoryTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";
    private const string ReviewerName = "Laura Bennett";

    [Fact]
    public async Task ReviewHistoryGrid_ForNeverReviewedDocument_IsPresentAndEmpty()
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

            Assert.True(await detail.IsReviewHistoryCardVisibleAsync(),
                "Expected the Review History card to be present even for a document that has never been reviewed");

            Assert.Equal(0, await detail.GetReviewHistoryRowCountAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReviewHistoryGrid_AfterCompletingOneReview_ShowsSingleRowWithReviewerNotesAndDate()
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

            var reviewNotes = $"Reviewed and confirmed still accurate {Guid.NewGuid():N}";
            await detail.CompleteReviewAsync(reviewNotes);

            Assert.Equal(1, await detail.WaitForReviewHistoryRowCountAsync(1));

            Assert.Equal(ReviewerName, await detail.GetReviewHistoryRowCellAsync(0, 1));
            Assert.Equal(reviewNotes, await detail.GetReviewHistoryRowCellAsync(0, 2));

            // Format ("d") is culture-dependent, so only assert the year we expect is present
            // rather than an exact dd/MM vs MM/dd rendering — same reasoning as
            // SharedDocumentVersionHistoryTests' Effective Date assertion.
            var reviewDateCell = await detail.GetReviewHistoryRowCellAsync(0, 0);
            Assert.Contains(DateTime.Today.Year.ToString(), reviewDateCell);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReviewHistoryGrid_AfterCompletingSecondReview_ShowsBothRowsWithNewestFirst()
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

            var firstReviewNotes = $"First review {Guid.NewGuid():N}";
            await detail.CompleteReviewAsync(firstReviewNotes);
            Assert.Equal(1, await detail.WaitForReviewHistoryRowCountAsync(1));

            var secondReviewNotes = $"Second review {Guid.NewGuid():N}";
            await detail.CompleteReviewAsync(secondReviewNotes);
            Assert.Equal(2, await detail.WaitForReviewHistoryRowCountAsync(2));

            // Sorted newest-first server-side (no client-side sort on top) — the second/most
            // recent review's row is expected at index 0, the first review's row at index 1.
            Assert.Equal(secondReviewNotes, await detail.GetReviewHistoryRowCellAsync(0, 2));
            Assert.Equal(firstReviewNotes, await detail.GetReviewHistoryRowCellAsync(1, 2));

            Assert.Equal(ReviewerName, await detail.GetReviewHistoryRowCellAsync(0, 1));
            Assert.Equal(ReviewerName, await detail.GetReviewHistoryRowCellAsync(1, 1));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReviewHistoryGrid_IsReadOnly_WithOnlyTheFourExpectedColumnsAndNoRowActions()
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

            var headers = await detail.GetReviewHistoryColumnHeadersAsync();
            Assert.Equal(["Review Date", "Reviewer", "Notes", "Previous Review Date"], headers);

            // Complete a review so there's an actual row to inspect for row-level action
            // controls, not just an empty grid's header row.
            await detail.CompleteReviewAsync($"Reviewed {Guid.NewGuid():N}");
            Assert.Equal(1, await detail.WaitForReviewHistoryRowCountAsync(1));

            Assert.Equal(0, await detail.GetReviewHistoryRowActionControlCountAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentVersionHistoryTests / SharedDocumentCompleteReviewTests),
    // with no Review Frequency configured — the "Review Document" button is available to an HR
    // Administrator regardless of Review Frequency (per SharedDocumentCompleteReviewTests'
    // "LoadDetailPage_ShowsReviewDocumentButtonForHrAdministrator"), so this file doesn't need it.
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
