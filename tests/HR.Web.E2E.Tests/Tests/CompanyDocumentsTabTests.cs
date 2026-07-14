using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the employee-facing "Company Documents" tab on My Profile
/// (MyProfileCompanyDocumentsTab.razor) — the published-shared-company-document view, distinct
/// from the personal "Documents" tab covered by SelfServiceDocumentTests. Covers the summary
/// tiles, the status filter, the per-card acknowledgement/"New" badges and click-through to the
/// detail page, and the "?tab=companyDocuments" deep link.
///
/// There is no seeded published SharedCompanyDocument (DocumentsModule.SeedDocumentsAsync only
/// seeds the older per-employee Document/EmployeeDocument records used by the personal Documents
/// tab), so each test that needs the tab non-empty uploads and publishes its own document(s) via
/// the HR-admin UI flow (same pattern as SharedDocumentUploadTests / SharedDocumentArchiveTests),
/// using Laura Bennett (HrAdministrator). A newly uploaded document has no audience rules, which
/// SharedCompanyDocumentAudienceMatcher treats as "all employees" — so it's visible to Tom
/// Williams (tom.williams@acme.example, employee ID 30000000-...-004), the employee persona used
/// throughout, without needing any explicit audience configuration.
/// </summary>
[Collection("E2E")]
public sealed class CompanyDocumentsTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string HrEmail  = "laura.bennett@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task CompanyDocumentsTab_LoadsSummaryTilesAndPublishedDocument()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        await UploadAndPublishDocumentAsync(title);

        await login.SwitchAccountAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenCompanyDocumentsTabAsync();

        Assert.Equal(0, await _page.Locator(".alert-danger").CountAsync());

        var totalAvailable = await profile.GetCompanyDocumentsSummaryTileValueAsync("Total Available");
        Assert.True(totalAvailable >= 1,
            $"Expected the 'Total Available' tile to show at least 1, got {totalAvailable}");

        Assert.True(await profile.HasCompanyDocumentCardAsync(title),
            "Expected the just-published document to appear as a card on the Company Documents tab");
    }

    [Fact]
    public async Task CompanyDocumentsTab_StatusFilter_ShowsOnlyMatchingDocuments()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var noAckTitle  = $"Test Policy {Guid.NewGuid():N}";
        var outstandingTitle = $"Test Policy {Guid.NewGuid():N}";

        await UploadAndPublishDocumentAsync(noAckTitle);
        await UploadAndPublishDocumentAsync(outstandingTitle,
            requiresAcknowledgement: true,
            acknowledgementDueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        await login.SwitchAccountAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenCompanyDocumentsTabAsync();

        // "All" (the default filter) shows both.
        Assert.True(await profile.HasCompanyDocumentCardAsync(noAckTitle));
        Assert.True(await profile.HasCompanyDocumentCardAsync(outstandingTitle));

        await profile.SetCompanyDocumentsStatusFilterAsync("Requires Action");

        Assert.True(await profile.HasCompanyDocumentCardAsync(outstandingTitle),
            "Expected the unacknowledged, acknowledgement-required document to remain under 'Requires Action'");
        Assert.False(await profile.HasCompanyDocumentCardAsync(noAckTitle),
            "Expected the document with no acknowledgement requirement to be filtered out under 'Requires Action'");

        var badge = await profile.GetCompanyDocumentAcknowledgementBadgeTextAsync(outstandingTitle);
        Assert.NotNull(badge);
        Assert.Contains("Acknowledgement Required", badge);

        // Switching back to "All" restores the full list.
        await profile.SetCompanyDocumentsStatusFilterAsync("All");

        Assert.True(await profile.HasCompanyDocumentCardAsync(noAckTitle));
        Assert.True(await profile.HasCompanyDocumentCardAsync(outstandingTitle));
    }

    [Fact]
    public async Task CompanyDocumentsTab_ShowsAcknowledgementBadge_AndNavigatesToDetailOnClick()
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
        await profile.OpenCompanyDocumentsTabAsync();

        var badge = await profile.GetCompanyDocumentAcknowledgementBadgeTextAsync(title);
        Assert.NotNull(badge);
        Assert.Contains("Acknowledgement Required", badge);

        await profile.ClickCompanyDocumentCardAsync(title);

        await _page.WaitForURLAsync(url => url.Contains("/shared-documents/published/"), new() { Timeout = 15_000 });
        Assert.Contains("/shared-documents/published/", _page.Url);
    }

    [Fact]
    public async Task CompanyDocumentsTab_DeepLinkQueryString_OpensAsActiveTab()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{TomId}/profile?tab=companyDocuments");
        await profile.WaitForLoadAsync();

        var activeTab = await profile.GetActiveTabNameAsync();
        Assert.Equal("Company Documents", activeTab);
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

            await dialog.Locator("span[role='combobox']").First.ClickAsync();
            await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
            await _page.Locator(".e-popup.e-ddl .e-list-item")
                .Filter(new() { HasText = "Policy" })
                .First
                .ClickAsync();

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
