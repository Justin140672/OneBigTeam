using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Documents tab on the self-service My Profile page.
///
/// Tom Williams (30000000-...-004) has:
///   - Seeded uploaded docs: Employment Contract, Offer Letter
///   - Seeded document request: Passport (b0000000-...-001, task a0000000-...-010)
/// </summary>
[Collection("E2E")]
public sealed class SelfServiceDocumentTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task SelfServiceDocumentsTab_ShowsSeededDocuments()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        var content = await _page.ContentAsync();

        Assert.True(
            content.Contains("Employment Contract", StringComparison.OrdinalIgnoreCase),
            "Expected 'Employment Contract' to appear on Tom's self-service Documents tab");

        Assert.True(
            content.Contains("Offer Letter", StringComparison.OrdinalIgnoreCase),
            "Expected 'Offer Letter' to appear on Tom's self-service Documents tab");
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_HasNoGeneralUploadButton_OnlyPerRequestUploadButtons()
    {
        // The admin-only bulk "Upload" button (Documents card header) never renders for
        // EmployeeSelfUpload — that part is unchanged. But each "Requested" row in the Document
        // Requests grid now gets its own contextual "Upload" button (EmployeeDocumentsTab.razor),
        // so the button isn't entirely absent anymore — Tom has exactly one open request
        // (Passport), so exactly one "Upload" button should be visible.
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        var uploadBtns = _page.GetByRole(AriaRole.Button, new() { Name = "Upload" });
        Assert.Equal(1, await uploadBtns.CountAsync());
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_HasNoDeleteButtonsAnywhere()
    {
        // Deleting a document is HR/manager-only (server-enforced by the "employee:manage"
        // policy) — the button shouldn't even appear on the self-service view.
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        var deleteBtns = _page.Locator("[title='Delete']");
        Assert.Equal(0, await deleteBtns.CountAsync());
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_ShowsRequestedDocumentsSection()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // "Requested Documents" (the self-service-only duplicate section) was removed — the
        // "Document Requests" section (shared with the admin employee profile view) is now the
        // only place this shows, so assert against that instead.
        var requestedSection = _page.Locator("[data-testid='admin-document-requests-section']");
        Assert.True(await requestedSection.IsVisibleAsync(),
            "Expected the 'Document Requests' section to be visible for Tom, who has a Passport request");

        var content = await requestedSection.TextContentAsync();
        Assert.Contains("Passport", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_UploadRequestedDocument_CompletesTheRequest()
    {
        // Tom's seeded Passport request is "Requested" — uploading against it via the grid row's
        // new "Upload" button (EmployeeDocumentsTab.razor, EmployeeSelfUpload branch) should mark
        // it Uploaded and remove the row's "Upload" action.
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.True(await profile.HasUploadButtonForDocumentRequestAsync("Passport"),
            "Expected an 'Upload' button on Tom's outstanding Passport request row");

        var tempFile = Path.Combine(Path.GetTempPath(), $"passport-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await profile.UploadRequestedDocumentAsync("Passport", tempFile);

            // The grid reloads after a successful upload — the request should no longer show an
            // "Upload" action (its status has moved on from "Requested").
            Assert.False(await profile.HasUploadButtonForDocumentRequestAsync("Passport"),
                "Expected the Passport request's 'Upload' button to disappear once the document has been uploaded");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_DownloadButton_IsVisibleAndClickable()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        await _page.WaitForSelectorAsync(".e-gridcontent td, .card-body td",
            new() { Timeout = 15_000 });

        var downloadBtn = _page.Locator("[title='Download']").First;
        Assert.True(await downloadBtn.IsVisibleAsync(),
            "Expected a Download button to be visible for Tom's seeded documents");

        // DownloadAsync calls JS window.open via Blazor Server interop — there is no browser-side
        // HTTP request to intercept. Spy on window.open before clicking so we can verify it fires.
        // Spy on window.open without calling the original — opening a file:// URI from an
        // HTTP origin is cross-origin blocked in Chromium and can destabilise the browser.
        await _page.EvaluateAsync(
            "window.__lastOpenedUrl = null; " +
            "window.open = (url, target) => { window.__lastOpenedUrl = url; return null; };");

        await downloadBtn.ClickAsync();

        await _page.WaitForFunctionAsync("window.__lastOpenedUrl !== null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var openedUrl = await _page.EvaluateAsync<string>("window.__lastOpenedUrl");
        Assert.False(string.IsNullOrEmpty(openedUrl),
            "Expected window.open to be called with a download URL after clicking Download");
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
