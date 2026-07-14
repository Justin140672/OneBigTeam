using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers "Add Review Frequency to Company Document": the field is selectable on the Upload
/// dialog (create), including the conditional "Custom Frequency (months)" input that only
/// appears when "Custom" is selected, and is editable afterwards via SharedDocumentDetail.razor's
/// header "Edit" button (EditSharedCompanyDocumentMetadataDialog.razor). Also covers the "None"
/// default: SharedDocumentDetail.razor only renders the Review Frequency row at all once it's
/// something other than "None".
///
/// Upload here follows the same UI flow already covered in SharedDocumentUploadTests — this file
/// does not re-assert unrelated upload-dialog validation (title/category/file required), only
/// what Review Frequency adds on top.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentReviewFrequencyTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task UploadDocument_WithQuarterlyReviewFrequency_DisplaysOnDetailPage()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewFrequencyLabel: "Quarterly");

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal("Quarterly", await detail.GetReviewFrequencyTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadDocument_WithCustomReviewFrequency_ShowsMonthsOnDetailPage()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewFrequencyLabel: "Custom", customMonths: 6);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            var reviewFrequencyText = await detail.GetReviewFrequencyTextAsync();
            Assert.Contains("Custom", reviewFrequencyText);
            Assert.Contains("every 6 months", reviewFrequencyText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadDocument_WithDefaultReviewFrequency_HidesRow_ThenEditingSetsIt()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // No reviewFrequencyLabel supplied — leaves the dropdown at its "None" default.
            await UploadDocumentAsync(title, tempFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Null(await detail.GetReviewFrequencyTextAsync());

            await detail.SetReviewFrequencyAsync("Yearly");

            Assert.Equal("Yearly", await detail.GetReviewFrequencyTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentArchiveTests), optionally selecting a Review
    // Frequency before submitting. Category is the first combobox in the dialog, Review Frequency
    // the second — same click-open/wait-for-popup/click-item interaction pattern used throughout
    // this test suite for Syncfusion dropdowns.
    private async Task UploadDocumentAsync(
        string title, string filePath, string? reviewFrequencyLabel = null, int? customMonths = null)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        await dialog.Locator("span[role='combobox']").First.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await _page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = "Policy" })
            .First
            .ClickAsync();

        if (reviewFrequencyLabel is not null)
        {
            await dialog.Locator("span[role='combobox']").Nth(1).ClickAsync();
            await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
            await _page.Locator(".e-popup.e-ddl .e-list-item")
                .Filter(new() { HasText = reviewFrequencyLabel })
                .First
                .ClickAsync();

            if (customMonths.HasValue)
            {
                var monthsInput = dialog.Locator(".col-md-6")
                    .Filter(new() { HasText = "Custom Frequency" })
                    .Locator("input");
                await monthsInput.FillAsync(customMonths.Value.ToString());
            }
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
