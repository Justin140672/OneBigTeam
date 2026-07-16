using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the two new Shared Documents *list* grid columns ("Review Frequency" and "Review
/// Owner") added to SharedDocuments.razor. Both fields were already visible on the detail page
/// (see SharedDocumentReviewFrequencyTests / SharedDocumentReviewOwnerTests) — this file only
/// covers their rendering on the list grid itself: correct values when set, blank cells when
/// unset, and the "SixMonthly" -> "Six Monthly" friendly-label mapping
/// (DocumentModels.ReviewFrequencyDisplay.Label) that the list grid's column template applies.
///
/// Column order in SharedDocuments.razor's GridColumns (0-based, matches DOM order of
/// ".e-rowcell" per row): 0=Title, 1=Category, 2=Version, 3=Status, 4=Effective Date,
/// 5=Next Review Date, 6=Review Frequency, 7=Review Owner, 8=Last Updated, 9=Updated By.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, and Marcus Diallo as a review owner candidate (a seeded Acme employee — see
/// EmployeesModule), matching the other Shared Documents E2E tests.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentListReviewColumnsTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";
    private const string MarcusDiallo = "Marcus Diallo";

    private const int ReviewFrequencyColumnIndex = 6;
    private const int ReviewOwnerColumnIndex = 7;

    [Fact]
    public async Task ListGrid_WithReviewFrequencyAndReviewOwner_ShowsBothInTheirColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(
                title, tempFile,
                reviewFrequencyLabel: "Quarterly",
                reviewOwnerNameFragment: MarcusDiallo);

            // Reload the list page fresh so the assertion exercises a real page load of the grid,
            // not just the in-memory state left over from the upload.
            await GoToListPageAsync();

            Assert.Equal("Quarterly", await GetListRowCellAsync(title, ReviewFrequencyColumnIndex));
            Assert.Equal(MarcusDiallo, await GetListRowCellAsync(title, ReviewOwnerColumnIndex));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ListGrid_WithNoReviewFrequencyOrReviewOwner_ShowsBlankCells()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // Neither reviewFrequencyLabel nor reviewOwnerNameFragment supplied — both fields are
            // left at their unset defaults ("None" / no owner selected).
            await UploadDocumentAsync(title, tempFile);

            await GoToListPageAsync();

            Assert.Equal(string.Empty, await GetListRowCellAsync(title, ReviewFrequencyColumnIndex));
            Assert.Equal(string.Empty, await GetListRowCellAsync(title, ReviewOwnerColumnIndex));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ListGrid_WithSixMonthlyReviewFrequency_DisplaysFriendlyLabel()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewFrequencyLabel: "Six Monthly");

            await GoToListPageAsync();

            Assert.Equal("Six Monthly", await GetListRowCellAsync(title, ReviewFrequencyColumnIndex));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private async Task GoToListPageAsync()
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
    }

    // Reads the 0-based columnIndex cell of the list grid row whose text contains title — same
    // ".e-row" filter + ".e-rowcell" Nth() pattern used by
    // SharedDocumentDetailPage.GetVersionRowCellAsync for the Version History grid on the detail
    // page.
    private async Task<string> GetListRowCellAsync(string title, int columnIndex)
    {
        var row = _page.Locator(".e-row").Filter(new() { HasText = title }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
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
        await categoryGroup.Locator("span[role='combobox']").First.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await _page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = "Policy" })
            .First
            .ClickAsync();

        if (reviewFrequencyLabel is not null)
        {
            var reviewFrequencyGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Review Frequency" });
            await reviewFrequencyGroup.Locator("span[role='combobox']").First.ClickAsync();
            await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
            await _page.Locator(".e-popup.e-ddl .e-list-item")
                .Filter(new() { HasText = reviewFrequencyLabel })
                .First
                .ClickAsync();

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
            await reviewOwnerGroup.Locator("span[role='combobox']").First.ClickAsync();
            await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

            // Type into the filter input — required for AllowFiltering dropdowns (same pattern as
            // EmployeeEditPage.SelectManagerAsync).
            var filterInput = _page.Locator(".e-popup.e-ddl:visible input.e-input").First;
            await filterInput.FillAsync(reviewOwnerNameFragment);
            await _page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
            await _page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
                .Filter(new() { HasText = reviewOwnerNameFragment })
                .First
                .ClickAsync();

            await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible",
                new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
                .ToHaveValueAsync(reviewOwnerNameFragment, new() { Timeout = 10_000 });

            // Same "displayed value can be ahead of the server-side ValueChanged commit" concern
            // as SharedDocumentDetailPage.SetReviewOwnerAsync — the file-write/SetInputFilesAsync
            // steps below normally provide enough of a gap for the round trip to land, but this
            // makes that explicit rather than incidental.
            await _page.WaitForTimeoutAsync(300);
        }

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
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
