using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Documents tab on the self-service My Profile page:
/// - Seeded documents are visible to the employee.
/// - The download action opens the document in a new tab (window.open).
/// - The upload button is hidden for employees (EmployeeSelfUpload=true hides it).
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

        // EmployeeDocumentsTab renders the Upload button only when !EmployeeSelfUpload.
        // The self-service view passes EmployeeSelfUpload="true", so Upload must be hidden.
        var uploadBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Upload" });
        Assert.False(await uploadBtn.IsVisibleAsync(),
            "Expected no 'Upload' button on the self-service Documents tab — upload is disabled for employees");
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

        // DownloadAsync calls JS.InvokeVoidAsync("window.open", url, "_blank") which opens a
        // popup tab rather than triggering a browser file download.  Listen for the popup
        // before clicking so we don't miss the event if it fires before we start waiting.
        var popupTask = _page.WaitForPopupAsync();

        // The download button has title="Download" (rendered by Syncfusion SfButton).
        var downloadBtn = _page.Locator("[title='Download']").First;
        await downloadBtn.ClickAsync();

        // The popup (new tab) should open within 15 seconds.
        var popup = await popupTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(popup);
        await popup.CloseAsync();
    }
}
