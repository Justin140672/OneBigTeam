using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class MyProfileDocumentsTabTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string HrEmail  = "laura.bennett@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task DocumentsTab_ShowsBothPersonalAndCompanyDocuments_WithCorrectSourceLabels()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var companyDocTitle = $"Test Policy {Guid.NewGuid():N}";
        await UploadAndPublishDocumentAsync(companyDocTitle);

        await login.SwitchAccountAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        Assert.Equal(0, await _page.Locator(".alert-danger").CountAsync());

        Assert.True(await profile.HasDocumentRowAsync("Employment Contract"),
            "Expected Tom's seeded personal document 'Employment Contract' to appear in the merged Documents grid");
        Assert.True(await profile.HasDocumentRowAsync(companyDocTitle),
            "Expected the just-published company document to appear in the merged Documents grid");

        Assert.Equal("Personal", await profile.GetDocumentRowSourceAsync("Employment Contract"));
        Assert.Equal("Company", await profile.GetDocumentRowSourceAsync(companyDocTitle));
    }

    [Fact]
    public async Task DocumentsTab_RendersExpectedColumnHeaders()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        var headers = await profile.GetDocumentsGridColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Title"));
        Assert.Contains(headers, h => h.Contains("Source"));
        Assert.Contains(headers, h => h.Contains("Type") && h.Contains("Category"));
        Assert.Contains(headers, h => h.Contains("Date"));
        Assert.Contains(headers, h => h.Contains("Status"));
    }

    [Fact]
    public async Task DocumentsTab_CompanyDocumentRow_ShowsAcknowledgementStatus_AndNavigatesToDetailOnClick()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));
        await UploadAndPublishDocumentAsync(title, requiresAcknowledgement: true, acknowledgementDueDate: dueDate);

        await login.SwitchAccountAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        var status = await profile.GetDocumentRowStatusTextAsync(title);
        Assert.NotNull(status);
        Assert.Contains("Acknowledgement Required", status);

        await profile.ClickDocumentTitleLinkAsync(title);

        await _page.WaitForURLAsync(url => url.Contains("/shared-documents/published/"), new() { Timeout = 15_000 });
        Assert.Contains("/shared-documents/published/", _page.Url);
    }

    [Fact]
    public async Task DocumentsTab_CompanyDocumentRow_ViewActionButton_NavigatesToDetail()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        await UploadAndPublishDocumentAsync(title);

        await login.SwitchAccountAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await profile.ClickDocumentRowActionAsync(title, "View");

        await _page.WaitForURLAsync(url => url.Contains("/shared-documents/published/"), new() { Timeout = 15_000 });
        Assert.Contains("/shared-documents/published/", _page.Url);
    }

    [Fact]
    public async Task DocumentsTab_PersonalDocumentRow_HasDownloadAction_NotView()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        var row = _page.Locator("[data-testid='my-profile-documents-grid-section'] .e-grid .e-row")
            .Filter(new() { HasText = "Employment Contract" }).First;

        Assert.True(await row.GetByRole(AriaRole.Button, new() { Name = "Download" }).IsVisibleAsync(),
            "Expected a Download button on the personal document row");
        Assert.Equal(0, await row.GetByRole(AriaRole.Button, new() { Name = "View" }).CountAsync());
    }

    private async Task<Guid> UploadAndPublishDocumentAsync(
        string title, bool requiresAcknowledgement = false, DateOnly? acknowledgementDueDate = null)
    {
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);
        var tempFile = Path.Combine(Path.GetTempPath(), $"company-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
            await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

            await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

            var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            await dialog.GetByPlaceholder("Document title").FillAsync(title);

            var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
            await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await dialog.Locator("input[type='file']").SetInputFilesAsync(tempFile);

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

            var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
            Assert.NotNull(href);
            var documentId = Guid.Parse(href!.Split('/').Last());

            await detail.GoToAsync(AcmeId, documentId);

            if (requiresAcknowledgement)
            {
                await detail.RequireAcknowledgementAsync(
                    acknowledgementDueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));
            }

            await detail.PublishAsync();

            return documentId;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
