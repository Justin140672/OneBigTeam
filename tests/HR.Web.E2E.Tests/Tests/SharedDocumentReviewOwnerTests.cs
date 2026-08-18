using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Review Owner" field on a Shared Company Document: an optional employee picker
/// (SfDropDownList, AllowFiltering + ShowClearButton, same server-side-search pattern as
/// EmployeeEmploymentTab's Manager picker — see EmployeeEditPage.SelectManagerAsync) that's
/// selectable on the Upload dialog (create) and editable afterwards via
/// SharedDocumentDetail.razor's header "Edit" button
/// (EditSharedCompanyDocumentMetadataDialog.razor). Also covers the unset default:
/// SharedDocumentDetail.razor only renders the Review Owner row once ReviewOwnerEmployeeId is set,
/// and clearing it via the dropdown's clear button removes the row again.
///
/// Upload here follows the same UI flow already covered in SharedDocumentUploadTests — this file
/// does not re-assert unrelated upload-dialog validation (title/category/file required), only
/// what Review Owner adds on top. Structured to mirror SharedDocumentReviewFrequencyTests, which
/// covers the sibling Review Frequency field the same way.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, and Marcus Diallo / James Okafor as review owner candidates (both seeded Acme
/// employees — see EmployeesModule), matching the other Shared Documents E2E tests.
/// </summary>
public sealed class SharedDocumentReviewOwnerTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";
    private const string MarcusDiallo = "Marcus Diallo";
    private const string JamesOkafor = "James Okafor";

    [Fact]
    public async Task UploadDocument_WithReviewOwner_DisplaysOnDetailPage()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewOwnerNameFragment: MarcusDiallo);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(MarcusDiallo, await detail.GetReviewOwnerTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadDocument_WithNoReviewOwner_HidesRow_ThenEditingAddsOne()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // No reviewOwnerNameFragment supplied — leaves the picker at its "None" default.
            await UploadDocumentAsync(title, tempFile);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Null(await detail.GetReviewOwnerTextAsync());

            await detail.SetReviewOwnerAsync(MarcusDiallo);

            Assert.Equal(MarcusDiallo, await detail.GetReviewOwnerTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EditDocument_ChangesExistingReviewOwner_DisplaysOnDetailPage()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewOwnerNameFragment: MarcusDiallo);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(MarcusDiallo, await detail.GetReviewOwnerTextAsync());

            await detail.SetReviewOwnerAsync(JamesOkafor);

            Assert.Equal(JamesOkafor, await detail.GetReviewOwnerTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EditDocument_ClearsReviewOwner_HidesRowOnDetailPage()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewOwnerNameFragment: MarcusDiallo);

            var documentId = await GetUploadedDocumentIdAsync(title);
            await detail.GoToAsync(AcmeId, documentId);

            Assert.Equal(MarcusDiallo, await detail.GetReviewOwnerTextAsync());

            await detail.ClearReviewOwnerAsync();

            Assert.Null(await detail.GetReviewOwnerTextAsync());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Uploads a shared document from the Shared Documents list page (same flow as
    // SharedDocumentUploadTests / SharedDocumentReviewFrequencyTests), optionally selecting a
    // Review Owner before submitting via the dialog's filterable employee picker. Both Category
    // and Review Owner are reached by scoping to their own ".col-md-6" field group (rather than by
    // combobox index) since Review Frequency's combobox can render before Category's — Category's
    // is gated behind an async data load while Review Frequency's isn't — and Review Owner sits
    // after the conditional "Custom Frequency (months)" field, whose presence would otherwise
    // shift a plain Nth() index.
    private async Task UploadDocumentAsync(
        string title, string filePath, string? reviewOwnerNameFragment = null)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        if (reviewOwnerNameFragment is not null)
        {
            var reviewOwnerGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
            await DropDownSelector.SelectAsync(_page, reviewOwnerGroup, reviewOwnerNameFragment);
        }

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as e.g. SharedDocumentReviewFrequencyTests).
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
