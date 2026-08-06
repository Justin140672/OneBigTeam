using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Audience" overview-card's "Edit" flow on SharedDocumentDetail.razor
/// (EditSharedCompanyDocumentAudienceDialog.razor): a freshly uploaded document defaults to "All
/// Employees" (no audience rules set), and scoping it to a department updates both the dialog and
/// the card's summary line to "Departments: {Name}" (per
/// SharedCompanyDocumentAudienceDescriber). "Engineering" is a seeded department for the Acme
/// company (also referenced e.g. by OrganisationChartTests), so no new seeding is needed here.
///
/// Upload here follows the same UI flow already covered in SharedDocumentUploadTests /
/// SharedDocumentVersionHistoryTests / SharedDocumentArchiveTests — this file does not re-assert
/// upload-dialog field validation, only what the Audience-edit dialog adds on top.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentAudienceTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task EditAudience_ScopeToDepartment_UpdatesAudienceSummary()
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

            Assert.Equal("All Employees", await detail.GetAudienceSummaryAsync());

            await detail.OpenEditAudienceDialogAsync();

            var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Audience" });

            // Drive the Departments SfMultiSelect (Mode="VisualMode.CheckBox", AllowFiltering)
            // the same way the single-select category dropdown is driven elsewhere in this test
            // suite: click the field to open its popup, wait for the popup, then click the target
            // list item — clicking anywhere on a checkbox-mode list item toggles its checkbox.
            var departmentsInput = dialog.GetByPlaceholder("Any department");
            await departmentsInput.ClickAsync();
            await _page.WaitForSelectorAsync(".e-popup:visible", new() { Timeout = 10_000 });
            await _page.Locator(".e-popup .e-list-item")
                .Filter(new() { HasText = "Engineering" })
                .First
                .ClickAsync();

            // Checkbox-mode multiselect popups stay open to allow further selections, so click a
            // neutral area of the dialog (its instructional paragraph) to close the popup via its
            // outside-click handler before reaching for the footer Save button.
            await dialog.Locator("p.text-muted.small").ClickAsync();

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

            await _page.WaitForFunctionAsync(
                "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
                null, new PageWaitForFunctionOptions { Timeout = 15_000 });

            Assert.Contains("Departments: Engineering", await detail.GetAudienceSummaryAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentVersionHistoryTests / SharedDocumentArchiveTests)
    // and leaves the browser on that list, with the new title visible in the grid so its row's
    // href can be read to discover the generated document id.
    private async Task UploadDocumentAsync(string title, string filePath)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        // Select a category via the shared Syncfusion SfDropDownList helper.
        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. EmploymentTypeEditCloseBehaviorTests
    // and SharedDocumentVersionHistoryTests).
    private async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        return Guid.Parse(href.Split('/').Last());
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
