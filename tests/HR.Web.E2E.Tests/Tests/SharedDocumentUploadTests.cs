using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Upload a shared company document" UI flow end-to-end: HR uploads a document
/// via SharedDocuments.razor / UploadSharedCompanyDocumentDialog.razor, and it appears in the
/// list as a Draft. Also verifies a Manager (who has shared-document:view-published but not
/// shared-document:manage) is redirected away from the page entirely, matching the
/// IsHrAdministrator-only page guard.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) whose company already has
/// seeded document categories (Policy, Handbook, Procedure, Form, Guidance, Health and Safety,
/// Other — see DocumentsModule.SeedDocumentsAsync) to pick from.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentUploadTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string HrEmail      = "laura.bennett@acme.example";
    private const string ManagerEmail = "james.okafor@acme.example";

    [Fact]
    public async Task HrAdministrator_CanUploadSharedDocument_AndSeeItInList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await _page.GotoAsync(_fixture.WebBaseUrl + "/companies/00000000-0000-0000-0000-000000000001/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var title = $"Test Policy {Guid.NewGuid():N}";
        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        // Select a category via the Syncfusion SfDropDownList (same interaction pattern used
        // throughout this test suite — click the combobox, wait for the popup, click the item).
        await dialog.Locator("span[role='combobox']").First.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await _page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = "Policy" })
            .First
            .ClickAsync();

        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await dialog.Locator("input[type='file']").SetInputFilesAsync(tempFile);

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

            // The grid re-fetches after a successful upload — the new title should appear,
            // and (per "new documents are created as drafts") its status column must read Draft.
            await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
            var row = _page.Locator(".e-row").Filter(new() { HasText = title });
            await Microsoft.Playwright.Assertions.Expect(row).ToContainTextAsync("Draft");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Manager_CannotReach_SharedDocumentsPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(ManagerEmail);

        await _page.GotoAsync(_fixture.WebBaseUrl + "/companies/00000000-0000-0000-0000-000000000001/shared-documents");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // IsHrAdministrator-only page guard redirects away — James (Employee+Manager) must not
        // land on or see the Shared Documents page.
        Assert.DoesNotContain("/shared-documents", _page.Url);
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
