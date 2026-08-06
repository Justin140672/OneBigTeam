using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Complete Review" / "Calculate Next Review Date" action on
/// SharedDocumentDetail.razor: the header "Review Document" button, the
/// CompleteSharedCompanyDocumentReviewDialog.razor confirmation dialog (its read-only document
/// metadata, required-notes validation, and Cancel), and the resulting state once a review is
/// completed — the "Last reviewed by … on …" segment on the page's footer summary line, and the
/// "Next Review Date" metadata value advancing according to the document's configured Review
/// Frequency (POST .../complete-review computes the next ReviewDate from ReviewFrequency: +1
/// month for Monthly, per CompleteSharedCompanyDocumentReviewHandler.ComputeNextReviewDate).
///
/// Uses a Monthly Review Frequency (rather than Quarterly/SixMonthly/Yearly, already covered as
/// the *input* field by SharedDocumentReviewFrequencyTests) purely so the "+1 month from today"
/// assertion here is simple and deterministic; the specific frequency choice is incidental to
/// this file's actual subject (completing a review), same as how SharedDocumentArchiveTests'
/// Publish setup isn't re-asserting Publish itself.
///
/// Upload here follows the same UI flow already covered in SharedDocumentUploadTests /
/// SharedDocumentReviewFrequencyTests / SharedDocumentReviewOwnerTests — this file does not
/// re-assert upload-dialog field validation, only what Complete Review adds on top. Creates its
/// own document per test (rather than reusing seeded company documents) to avoid mutating shared
/// seed data, matching the convention already used by the other Shared Documents E2E test files.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, and Marcus Diallo as a review owner (a seeded Acme employee — see EmployeesModule),
/// matching the other Shared Documents E2E tests.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentCompleteReviewTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";
    private const string MarcusDiallo = "Marcus Diallo";
    private const string PolicyCategory = "Policy";

    [Fact]
    public async Task LoadDetailPage_ShowsReviewDocumentButtonForHrAdministrator()
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

            Assert.True(await detail.IsReviewButtonVisibleAsync(),
                "Expected the Review Document button to be visible for an HR Administrator");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task OpenReviewDialog_ShowsCurrentDocumentMetadataReadOnly()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(
                title, tempFile,
                reviewFrequencyLabel: "Monthly",
                reviewOwnerNameFragment: MarcusDiallo);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            var expectedReviewDate = await detail.GetReviewDateTextAsync();
            Assert.NotNull(expectedReviewDate);

            await detail.OpenReviewDialogAsync();

            Assert.Equal(title, await detail.GetReviewDialogMetadataRowAsync("Title"));
            Assert.Equal(PolicyCategory, await detail.GetReviewDialogMetadataRowAsync("Category"));
            Assert.Equal(expectedReviewDate, await detail.GetReviewDialogMetadataRowAsync("Next Review Date"));
            Assert.Equal("Monthly", await detail.GetReviewDialogMetadataRowAsync("Review Frequency"));
            Assert.Equal(MarcusDiallo, await detail.GetReviewDialogMetadataRowAsync("Review Owner"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SubmitReview_WithBlankNotes_ShowsValidationError_AndKeepsDialogOpen()
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

            await detail.OpenReviewDialogAsync();
            await detail.ClickReviewConfirmAsync();

            Assert.Equal("Review notes are required.", await detail.GetReviewValidationErrorAsync());
            Assert.True(await detail.IsReviewDialogOpenAsync(),
                "Expected the Complete Review dialog to stay open when the notes are blank");

            // Whitespace-only notes must be rejected the same way as truly blank notes.
            await detail.FillReviewNotesAsync("   ");
            await detail.ClickReviewConfirmAsync();

            Assert.Equal("Review notes are required.", await detail.GetReviewValidationErrorAsync());
            Assert.True(await detail.IsReviewDialogOpenAsync(),
                "Expected the Complete Review dialog to stay open when the notes are whitespace-only");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CompleteReview_WithValidNotes_UpdatesFooterAndAdvancesNextReviewDate()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // Monthly, so the completed review's "next review date" is simply "today + 1 month" —
            // deterministic regardless of whatever Next Review Date was originally uploaded with.
            await UploadDocumentAsync(title, tempFile, reviewFrequencyLabel: "Monthly");

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            var reviewNotes = $"Reviewed and confirmed still accurate {Guid.NewGuid():N}";
            await detail.CompleteReviewAsync(reviewNotes);

            Assert.False(await detail.IsReviewDialogOpenAsync(),
                "Expected the Complete Review dialog to close after a successful submission");

            // SharedDocumentDetail.razor renders LastReviewedAt as "d MMM yyyy" and
            // LastReviewedByName ahead of it — see the footer <p>'s "· Last reviewed by …" clause.
            // Note: this assertion is date-based and would be flaky if the test happened to
            // straddle midnight between upload and this check (server clock advancing to the next
            // day mid-test), though in practice the flow completes in well under a second.
            // "Dialog closed" (asserted above) is an unreliable proxy for "the page's post-close
            // reload has landed in the DOM" — the two can arrive in separate render batches. Assert
            // directly on the footer's own content with Playwright's auto-retrying Expect instead
            // of a one-shot read, so the assertion itself waits out that gap.
            var expectedReviewedOn = DateTime.Today.ToString("d MMM yyyy");
            await Assertions.Expect(detail.FooterSummary)
                .ToContainTextAsync($"Last reviewed by Laura Bennett on {expectedReviewedOn}", new() { Timeout = 15_000 });

            // SharedDocumentDetail.razor renders ReviewDate as "d MMMM yyyy" (full month name) —
            // distinct from the footer's "d MMM yyyy" (abbreviated) used for LastReviewedAt above.
            var expectedNextReviewDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(1).ToString("d MMMM yyyy");
            Assert.Equal(expectedNextReviewDate, await detail.GetReviewDateTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CancelReviewDialog_ClosesWithoutSubmitting_AndDocumentUnchanged()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewFrequencyLabel: "Monthly");

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            var reviewDateBeforeCancel = await detail.GetReviewDateTextAsync();
            var footerBeforeCancel     = await detail.GetFooterSummaryTextAsync();

            await detail.OpenReviewDialogAsync();
            await detail.FillReviewNotesAsync("This should never be saved");
            await detail.ClickReviewCancelAsync();

            Assert.False(await detail.IsReviewDialogOpenAsync(),
                "Expected the Complete Review dialog to close after clicking Cancel");

            // Cancelling doesn't refetch the page's detail model, so re-navigating (a fresh load,
            // not just in-memory state) is the more convincing proof nothing was persisted server-side.
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(reviewDateBeforeCancel, await detail.GetReviewDateTextAsync());
            var footerAfterCancel = await detail.GetFooterSummaryTextAsync();
            Assert.Equal(footerBeforeCancel, footerAfterCancel);
            Assert.DoesNotContain("Last reviewed by", footerAfterCancel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentReviewFrequencyTests / SharedDocumentReviewOwnerTests),
    // optionally selecting a Review Frequency (+ its required Next Review Date once the frequency
    // isn't "None") and/or a Review Owner before submitting. Category, Review Frequency, and
    // Review Owner are each reached by scoping to their own ".col-md-6" field group (rather than by
    // combobox index) since Review Frequency's combobox can render before Category's — Category's
    // is gated behind an async data load while Review Frequency's isn't — and Review Owner sits
    // after the conditional "Custom Frequency (months)" field, whose presence would otherwise
    // shift a plain Nth() index.
    private async Task UploadDocumentAsync(
        string title, string filePath,
        string? reviewFrequencyLabel = null,
        string? reviewOwnerNameFragment = null)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, PolicyCategory);

        if (reviewFrequencyLabel is not null)
        {
            var reviewFrequencyGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Review Frequency" });
            await DropDownSelector.SelectAsync(_page, reviewFrequencyGroup, reviewFrequencyLabel);

            var reviewDateInput = dialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Next Review Date" })
                .Locator(".e-date-wrapper input.e-input");
            await reviewDateInput.ClickAsync();
            await reviewDateInput.FillAsync(DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("dd/MM/yyyy"));
            await _page.Keyboard.PressAsync("Tab");
        }

        if (reviewOwnerNameFragment is not null)
        {
            var reviewOwnerGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
            await DropDownSelector.SelectAsync(_page, reviewOwnerGroup, reviewOwnerNameFragment);
        }

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
