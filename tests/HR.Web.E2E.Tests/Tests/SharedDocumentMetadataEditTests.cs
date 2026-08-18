using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers editing a Shared Company Document's core metadata — Title, Description, and Category
/// together — via the page header's "Edit" button and
/// EditSharedCompanyDocumentMetadataDialog.razor. SharedDocumentAudienceTests and
/// SharedDocumentReviewOwnerTests already cover the Audience and Review Owner fields of the same
/// dialog individually; this file exercises the remaining core fields in one combined edit.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests.
/// </summary>
public sealed class SharedDocumentMetadataEditTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task EditDocument_ChangesTitleDescriptionAndCategory_PersistsAfterReload()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var originalTitle = $"Test Policy {Guid.NewGuid():N}";
        var updatedTitle       = $"Updated Handbook {Guid.NewGuid():N}";
        var updatedDescription = $"Updated description {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");

        try
        {
            await UploadDocumentAsync(originalTitle, tempFile);

            var documentId = await GetUploadedDocumentIdAsync(originalTitle);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(originalTitle, await detail.GetTitleAsync());
            Assert.Equal("Policy", await detail.GetCategoryAsync());

            await detail.EditTitleDescriptionCategoryAsync(updatedTitle, updatedDescription, "Handbook");

            Assert.Equal(updatedTitle, await detail.GetTitleAsync());
            Assert.Equal(updatedDescription, await detail.GetDescriptionAsync());
            Assert.Equal("Handbook", await detail.GetCategoryAsync());

            // ── Reload and verify the changes persisted server-side ────────────
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(updatedTitle, await detail.GetTitleAsync());
            Assert.Equal(updatedDescription, await detail.GetDescriptionAsync());
            Assert.Equal("Handbook", await detail.GetCategoryAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page with the "Policy" category —
    // same flow as SharedDocumentUploadTests/SharedDocumentReviewOwnerTests.
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

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. SharedDocumentReviewOwnerTests).
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
