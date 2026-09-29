using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedDocumentExpireTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task MarkExpired_OnPublishedDocument_OpensDialogWithWording_CancelLeavesUnchanged()
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

            await detail.PublishAsync();
            Assert.Equal("Published", await detail.GetStatusAsync());
            Assert.True(await detail.IsExpireButtonVisibleAsync(),
                "Expected the Mark Expired button to be visible for a Published document");
            Assert.True(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to remain visible alongside Mark Expired");

            await detail.OpenExpireDialogAsync();
            var bodyText = await detail.GetExpireDialogBodyTextAsync();
            Assert.Contains(title, bodyText);
            Assert.Contains("cancel any outstanding review tasks", bodyText);
            Assert.Contains("This cannot be undone", bodyText);

            await detail.ClickExpireCancelAsync();
            Assert.False(await detail.IsExpireDialogOpenAsync(),
                "Expected the Mark Expired dialog to close on Cancel");
            Assert.Equal("Published", await detail.GetStatusAsync());
            Assert.True(await detail.IsExpireButtonVisibleAsync(),
                "Expected the Mark Expired button to remain visible after cancelling");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MarkExpired_ConfirmsSuccessfully_TransitionsToExpiredAndHidesActionButtons()
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

            await detail.PublishAsync();
            Assert.Equal("Published", await detail.GetStatusAsync());

            await detail.ExpireAsync();

            Assert.Equal("Expired", await detail.GetStatusAsync());
            Assert.False(await detail.IsExpireButtonVisibleAsync(),
                "Expected the Mark Expired button to disappear once the document is Expired");
            Assert.False(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to also disappear once the document is Expired");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ArchivedDocument_HidesMarkExpiredButton_AndExpiredDocument_HidesArchiveButton()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var archivedTitle    = $"Test Policy {Guid.NewGuid():N}";
        var archivedTempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var expiredTitle      = $"Test Policy {Guid.NewGuid():N}";
        var expiredTempFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(archivedTitle, archivedTempFile);
            var archivedDocumentId = await GetUploadedDocumentIdAsync(archivedTitle);
            await detail.GoToAsync(AcmeId, archivedDocumentId);

            await detail.ArchiveAsync("No longer applicable");
            Assert.Equal("Archived", await detail.GetStatusAsync());
            Assert.False(await detail.IsExpireButtonVisibleAsync(),
                "Expected the Mark Expired button to be hidden once the document is Archived");

            await UploadDocumentAsync(expiredTitle, expiredTempFile);
            var expiredDocumentId = await GetUploadedDocumentIdAsync(expiredTitle);
            await detail.GoToAsync(AcmeId, expiredDocumentId);

            await detail.ExpireAsync();
            Assert.Equal("Expired", await detail.GetStatusAsync());
            Assert.False(await detail.IsArchiveButtonVisibleAsync(),
                "Expected the Archive button to be hidden once the document is Expired");
        }
        finally
        {
            if (File.Exists(archivedTempFile)) File.Delete(archivedTempFile);
            if (File.Exists(expiredTempFile)) File.Delete(expiredTempFile);
        }
    }

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
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    private async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
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
