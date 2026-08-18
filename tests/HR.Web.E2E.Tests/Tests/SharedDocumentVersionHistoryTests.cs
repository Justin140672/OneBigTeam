using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Version History grid extension on SharedDocumentDetail.razor: the "Publication
/// Status" and "Effective Date" columns, and the per-version "Download" link that hits
/// api/companies/{id}/shared-documents/{id}/versions/{n}/download.
///
/// The shared-document page itself (navigation, the HR-administrator-only guard, uploading a
/// document, and the "Upload New Version" dialog's own field validation) already has coverage in
/// SharedDocumentUploadTests — this file is scoped narrowly to the version-history grid built on
/// top of that pre-existing flow, and does not duplicate upload/publish/audience/acknowledgement
/// coverage.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching SharedDocumentUploadTests.
/// </summary>
public sealed class SharedDocumentVersionHistoryTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task VersionHistoryGrid_ShowsPublicationStatusEffectiveDateAndDownloadLink_ForCurrentVersion()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, effectiveDateDdMmYyyy: "15/03/2026");

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            var headers = await detail.GetVersionColumnHeadersAsync();
            Assert.Contains(headers, h => h.Contains("Publication Status"));
            Assert.Contains(headers, h => h.Contains("Effective Date"));
            Assert.Contains(headers, h => h.Contains("Download"));

            Assert.Equal(1, await detail.WaitForVersionRowCountAsync(1));

            var fileNameFragment = Path.GetFileName(tempFile);

            // Newly uploaded documents start as Draft (per "new documents are created as
            // drafts", also asserted at list level in SharedDocumentUploadTests) — the current
            // version's Publication Status must reflect that, not a hardcoded "Published"/etc.
            Assert.Equal("1", await detail.GetVersionRowCellAsync(fileNameFragment, 0));
            Assert.Equal("Draft", await detail.GetVersionRowCellAsync(fileNameFragment, 1));

            // Format ("d") is culture-dependent, so only assert the year we set is present
            // rather than asserting an exact dd/MM vs MM/dd rendering.
            var effectiveDateCell = await detail.GetVersionRowCellAsync(fileNameFragment, 5);
            Assert.Contains("2026", effectiveDateCell);

            var href = await detail.GetVersionDownloadHrefAsync(fileNameFragment);
            Assert.NotNull(href);
            Assert.Contains($"shared-documents/{documentId}/versions/1/download", href);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VersionHistoryGrid_AfterUploadingNewVersion_ShowsCurrentAndSupersededRows()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title      = $"Test Policy {Guid.NewGuid():N}";
        var firstFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-v1-{Guid.NewGuid():N}.pdf");
        var secondFile = Path.Combine(Path.GetTempPath(), $"shared-doc-v2-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, firstFile, effectiveDateDdMmYyyy: null);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);
            Assert.Equal(1, await detail.WaitForVersionRowCountAsync(1));

            await File.WriteAllBytesAsync(secondFile, BuildTestPdf());
            await detail.UploadNewVersionAsync("Second version content", secondFile);

            Assert.Equal(2, await detail.WaitForVersionRowCountAsync(2));

            var firstFileNameFragment  = Path.GetFileName(firstFile);
            var secondFileNameFragment = Path.GetFileName(secondFile);

            // v1 is no longer the document's current version — its row must read "Superseded"
            // regardless of the document's own (still-Draft) status.
            Assert.Equal("1", await detail.GetVersionRowCellAsync(firstFileNameFragment, 0));
            Assert.Equal("Superseded", await detail.GetVersionRowCellAsync(firstFileNameFragment, 1));

            var firstHref = await detail.GetVersionDownloadHrefAsync(firstFileNameFragment);
            Assert.NotNull(firstHref);
            Assert.Contains($"shared-documents/{documentId}/versions/1/download", firstHref);

            // v2 is now the current version — its Publication Status tracks the document's live
            // status (still "Draft"; this test never publishes).
            Assert.Equal("2", await detail.GetVersionRowCellAsync(secondFileNameFragment, 0));
            Assert.Equal("Draft", await detail.GetVersionRowCellAsync(secondFileNameFragment, 1));

            var secondHref = await detail.GetVersionDownloadHrefAsync(secondFileNameFragment);
            Assert.NotNull(secondHref);
            Assert.Contains($"shared-documents/{documentId}/versions/2/download", secondHref);
        }
        finally
        {
            if (File.Exists(firstFile)) File.Delete(firstFile);
            if (File.Exists(secondFile)) File.Delete(secondFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests) and leaves the browser on that list, with the new title visible
    // in the grid so its row's href can be read to discover the generated document id.
    private async Task UploadDocumentAsync(string title, string filePath, string? effectiveDateDdMmYyyy)
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

        if (effectiveDateDdMmYyyy is not null)
        {
            // The Effective Date picker is the first of two SfDatePicker fields (Effective Date,
            // then Review Date) in this dialog.
            var effectiveDateInput = dialog.Locator(".e-date-wrapper input.e-input").First;
            await effectiveDateInput.ClickAsync();
            await effectiveDateInput.FillAsync(effectiveDateDdMmYyyy);
            await _page.Keyboard.PressAsync("Tab");
        }

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. EmploymentTypeEditCloseBehaviorTests).
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
