using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the merged "Documents" tab on My Profile (MyProfileDocumentsTab.razor), which combines
/// the employee's personal documents and published company-wide documents into a single grid —
/// replacing what used to be two separate tabs ("Documents" and "Company Documents", see the
/// now-removed MyProfileCompanyDocumentsTab.razor). Covers: both sources appearing together in the
/// one grid, the "Source" column correctly labelling each row ("Personal"/"Company"), per-source
/// row actions (Download for a personal row, View/click-through for a company row), and the grid's
/// column headers. Personal-document-only behaviour (upload dialog, document requests, delete
/// permissions) remains covered by SelfServiceDocumentTests — this class only covers what's new/
/// changed by the merge.
///
/// There is no seeded published SharedCompanyDocument (DocumentsModule.SeedDocumentsAsync only
/// seeds the older per-employee Document/EmployeeDocument records used by personal documents), so
/// each test that needs a company-sourced row uploads and publishes its own document via the
/// HR-admin UI flow (same pattern as SharedDocumentUploadTests / SharedDocumentArchiveTests), using
/// Laura Bennett (HrAdministrator). A newly uploaded document has no audience rules, which
/// SharedCompanyDocumentAudienceMatcher treats as "all employees" — so it's visible to Tom Williams
/// (tom.williams@acme.example, employee ID 30000000-...-004), the employee persona used throughout,
/// without needing any explicit audience configuration. Tom also has seeded personal documents
/// (Employment Contract, Offer Letter — see SelfServiceDocumentTests), giving every test in this
/// class a mix of both sources without needing to seed a personal document itself.
/// </summary>
public sealed class MyProfileDocumentsTabTests(CrossUserFixture fixture) : CrossUserDocumentsAndRequestsTestBase(fixture)
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

        // Personal (seeded — see SelfServiceDocumentTests' class remarks) and the just-published
        // company document must both appear in the same grid.
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

        // "View" is the company-row Actions-column button (see MyProfileDocumentsTab.razor's
        // final GridColumn Template) — distinct from "Download", the personal-row equivalent.
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

    /// <summary>
    /// Uploads a shared document from the Shared Documents list page as HR (same flow as
    /// SharedDocumentUploadTests / SharedDocumentArchiveTests), optionally turning on
    /// acknowledgement requirements via the detail page's "Edit" flow, then publishes it. Assumes
    /// the caller is already logged in as an HrAdministrator. Returns to the Shared Documents
    /// detail page for the newly published document.
    /// </summary>
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
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

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

    // %PDF- followed by padding, so magic-byte content validation passes.
    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
