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
    public async Task SelfServiceDocumentsTab_HasNoUploadButtonsAnywhere()
    {
        // Documents are uploaded via tasks only — no Upload button should appear anywhere on this tab.
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
        Assert.Equal(0, await uploadBtns.CountAsync());
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

        var requestedSection = _page.Locator("[data-testid='requested-docs-section']");
        Assert.True(await requestedSection.IsVisibleAsync(),
            "Expected the 'Requested Documents' section to be visible for Tom, who has a Passport request");

        var content = await requestedSection.TextContentAsync();
        Assert.Contains("Passport", content, StringComparison.OrdinalIgnoreCase);
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
}
