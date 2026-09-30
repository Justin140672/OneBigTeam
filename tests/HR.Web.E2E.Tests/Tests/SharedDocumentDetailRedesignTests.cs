using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedDocumentDetailRedesignTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task MoreActionsMenu_ForDraftDocument_ListsArchiveMarkExpiredAndAuditHistory()
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

            Assert.True(await detail.IsArchiveButtonVisibleAsync(),
                "Expected 'Archive' to be listed in the More actions menu for a Draft document");
            Assert.True(await detail.IsExpireButtonVisibleAsync(),
                "Expected 'Mark Expired' to be listed in the More actions menu for a Draft document");

            await detail.OpenAuditHistoryDialogAsync();
            Assert.True(await detail.IsAuditHistoryDialogOpenAsync(),
                "Expected the Audit History dialog to open from the More actions menu");
            await detail.CloseAuditHistoryDialogAsync();
            Assert.False(await detail.IsAuditHistoryDialogOpenAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MoreActionsMenu_ForArchivedDocument_OnlyListsAuditHistory()
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

            await detail.ArchiveAsync("No longer applicable");
            Assert.Equal("Archived", await detail.GetStatusAsync());

            Assert.False(await detail.IsArchiveButtonVisibleAsync(),
                "Expected 'Archive' to be gone from the More actions menu for an Archived document");
            Assert.False(await detail.IsExpireButtonVisibleAsync(),
                "Expected 'Mark Expired' to be gone from the More actions menu for an Archived document");

            await detail.OpenAuditHistoryDialogAsync();
            Assert.True(await detail.IsAuditHistoryDialogOpenAsync(),
                "Expected 'Audit History' to remain available in the More actions menu for an Archived document");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RenamedEditButtons_EachOpenTheirOwnDialog()
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

            await detail.OpenMetadataDialogAsync();
            Assert.True(await detail.IsMetadataDialogOpenAsync(),
                "Expected 'Edit details' to open the Edit Document Metadata dialog");
            await detail.CloseMetadataDialogAsync();

            await detail.OpenAudienceDialogAsync();
            Assert.True(await detail.IsAudienceDialogOpenAsync(),
                "Expected 'Edit audience' to open the Edit Document Audience dialog");
            await detail.CloseAudienceDialogAsync();

            await detail.OpenEditAcknowledgementDialogAsync();
            Assert.True(await detail.IsAcknowledgementDialogOpenAsync(),
                "Expected 'Edit acknowledgement settings' to open the Edit Acknowledgement Settings dialog");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VersionDetailPopup_ShowsNoteRequiredAckAndEffectiveDate_ForAVersionRow()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, effectiveDateDdMmYyyy: "15/03/2026");

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(1, await detail.WaitForVersionRowCountAsync(1));

            var fileNameFragment = Path.GetFileName(tempFile);
            var (note, requiredAck, effectiveDate) = await detail.GetVersionDetailAsync(fileNameFragment);

            Assert.Equal("—", note);
            Assert.Equal("No", requiredAck);
            Assert.Contains("2026", effectiveDate);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private async Task UploadDocumentAsync(string title, string filePath, string? effectiveDateDdMmYyyy = null)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        if (effectiveDateDdMmYyyy is not null)
        {
            var effectiveDateInput = dialog.Locator(".e-date-wrapper input.e-input").First;
            await effectiveDateInput.ClickAsync();
            await effectiveDateInput.FillAsync(effectiveDateDdMmYyyy);
            await _page.Keyboard.PressAsync("Tab");
        }

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        try
        {
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "diag");
                Directory.CreateDirectory(dir);
                var stamp = $"{DateTime.UtcNow:HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}_upload-dialog-stuck";
                await _page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{stamp}.png"), FullPage = true });
                await File.WriteAllTextAsync(
                    Path.Combine(dir, $"{stamp}.html"),
                    $"URL: {_page.Url}\n\n=== Dialog text ===\n{await dialog.InnerTextAsync()}\n\n=== Dialog HTML ===\n{await dialog.InnerHTMLAsync()}");
            }
            catch { /* diagnostics only */ }
            throw;
        }

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    private async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
        await _page.RevealGridRowAsync(title);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        return Guid.Parse(href.Split('/').Last());
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
