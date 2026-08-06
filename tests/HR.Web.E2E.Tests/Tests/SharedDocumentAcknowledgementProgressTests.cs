using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the acknowledgement-progress screen
/// (SharedDocumentAcknowledgementProgress.razor, /companies/{companyId}/shared-documents/{documentId}/acknowledgement-progress):
/// the five summary tiles (Total Assigned, Acknowledged, Outstanding, Overdue, Acknowledgement
/// Rate) and the employee grid populated once a document that requires acknowledgement is
/// published. Publishing a document with RequiresAcknowledgement=true creates acknowledgement
/// assignments for all eligible employees — existing product behavior already covered by unit
/// tests, so this test does not itself acknowledge anything, only asserts the resulting progress
/// view renders. Only reachable for HrAdministrator (SharedDocumentAcknowledgementProgress.razor
/// redirects otherwise), so this file uses Laura Bennett like the other Shared Documents E2E
/// tests.
///
/// Upload, RequireAcknowledgementAsync, and Publish here follow the same UI flows already covered
/// in SharedDocumentUploadTests / SharedDocumentArchiveTests / SharedDocumentPublishTests — this
/// file does not re-assert upload-dialog field validation or the Publish/Archive flows
/// themselves, only what the acknowledgement-progress screen adds on top.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentAcknowledgementProgressTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AcknowledgementProgress_ShowsSummaryTilesAndEmployeeGrid()
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

            await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
            await detail.PublishAsync();

            await detail.GoToAcknowledgementProgressAsync(AcmeId, documentId);

            var totalAssignedTile = _page.Locator(".overview-card").Filter(new() { HasText = "Total Assigned" });
            var totalAssignedValue = await totalAssignedTile.Locator(".fs-4.fw-semibold").InnerTextAsync();
            Assert.True(int.TryParse(totalAssignedValue.Trim(), out var totalAssigned),
                $"Expected the Total Assigned tile to show a number, got '{totalAssignedValue}'");
            Assert.True(totalAssigned > 0, "Expected the Total Assigned tile to show a count greater than 0");

            var outstandingTile = _page.Locator(".overview-card").Filter(new() { HasText = "Outstanding" });
            await Assertions.Expect(outstandingTile).ToBeVisibleAsync();
            var outstandingValue = await outstandingTile.Locator(".fs-4.fw-semibold").InnerTextAsync();
            Assert.True(int.TryParse(outstandingValue.Trim(), out _),
                $"Expected the Outstanding tile to show a numeric value, got '{outstandingValue}'");

            var rateTile = _page.Locator(".overview-card").Filter(new() { HasText = "Acknowledgement Rate" });
            await Assertions.Expect(rateTile).ToBeVisibleAsync();

            var rowCount = await _page.Locator(".e-grid .e-row").CountAsync();
            Assert.True(rowCount > 0, "Expected the acknowledgement-progress employee grid to have at least one row");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentVersionHistoryTests / SharedDocumentArchiveTests)
    // and leaves the browser on that list, with the new title visible in the grid so its row's
    // href can be read to discover the generated document id.
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
