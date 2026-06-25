using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Documents tab on the self-service My Profile page:
/// - Seeded documents are visible to the employee.
/// - The download action initiates a file download.
/// - The upload button is present for employees.
/// </summary>
[Collection("E2E")]
public sealed class SelfServiceDocumentTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public SelfServiceDocumentTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task SelfServiceDocumentsTab_ShowsSeededDocuments()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to Tom's self-service profile ────────────────────
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        // Wait for spinner to disappear.
        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // ── Step 3: Tom's seeded documents should be visible ──────────────────
        // Tom has "Employment Contract" and "Offer Letter" assigned in the seed data.
        var content = await _page.ContentAsync();

        Assert.True(
            content.Contains("Employment Contract", StringComparison.OrdinalIgnoreCase),
            "Expected 'Employment Contract' to appear on Tom's self-service Documents tab");

        Assert.True(
            content.Contains("Offer Letter", StringComparison.OrdinalIgnoreCase),
            "Expected 'Offer Letter' to appear on Tom's self-service Documents tab");
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_ShowsUploadButton()
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

        // The self-service documents tab renders with EmployeeSelfUpload="true",
        // so an "Upload" button should be visible.
        var uploadBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Upload" });
        Assert.True(await uploadBtn.IsVisibleAsync(),
            "Expected an 'Upload' button to be visible on the self-service Documents tab");
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_DownloadButton_InitiatesDownload()
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

        // Wait for at least one row to appear in the documents grid.
        await _page.WaitForSelectorAsync(".e-gridcontent td, .card-body td",
            new() { Timeout = 15_000 });

        // Start listening for a download before clicking.
        var downloadTask = _page.WaitForDownloadAsync();

        // Click the first download icon button (fa-download).
        var downloadBtn = _page.Locator("button[title*='ownload'], button .fa-download")
            .First;
        await downloadBtn.ClickAsync();

        // The download should start within 15 seconds.
        var download = await downloadTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(string.IsNullOrEmpty(download.SuggestedFilename),
            "Expected a non-empty filename for the downloaded document");
    }
}
