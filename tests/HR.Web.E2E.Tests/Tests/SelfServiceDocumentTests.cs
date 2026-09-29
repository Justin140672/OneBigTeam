using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SelfServiceDocumentTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";
    private const string HrAdminEmail = "laura.bennett@acme.example";

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
    public async Task SelfServiceDocumentsTab_HasGeneralUploadButton_PlusOnePerRequestUploadButton()
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

        var requestRows = _page.Locator("[data-testid='my-profile-document-requests-section'] tbody tr");
        var requestRowCount = await requestRows.CountAsync();
        var uploadButtonsInRequests = 0;
        for (var i = 0; i < requestRowCount; i++)
        {
            if (await requestRows.Nth(i).GetByRole(AriaRole.Button, new() { Name = "Upload" }).IsVisibleAsync())
                uploadButtonsInRequests++;
        }

        var uploadBtns = _page.GetByRole(AriaRole.Button, new() { Name = "Upload" });
        Assert.Equal(1 + uploadButtonsInRequests, await uploadBtns.CountAsync());
        Assert.True(uploadButtonsInRequests >= 1,
            "Expected at least one per-request 'Upload' button for Tom's outstanding Passport request");
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_HasNoDeleteButtonsAnywhere()
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

        var requestedSection = _page.Locator("[data-testid='my-profile-document-requests-section']");
        Assert.True(await requestedSection.IsVisibleAsync(),
            "Expected the 'Document Requests' section to be visible for Tom, who has a Passport request");

        var content = await requestedSection.TextContentAsync();
        Assert.Contains("Passport", content, StringComparison.OrdinalIgnoreCase);
    }

    private static (Guid EmployeeId, string Email, string LastName) CreateEmployeeAsync()
    {
        var seeded = SeededE2eEmployees.SelfServiceDocument;
        return (seeded.EmployeeId, seeded.Email, seeded.LastName);
    }

    private async Task EnsureEmployeeLoginAsync(Guid employeeId, string email, string lastName)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        HttpResponseMessage? response = null;
        string? body = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            response = await http.PostAsJsonAsync("/api/dev/ensure-employee-login", new
            {
                EmployeeId = employeeId,
                CompanyId  = AcmeId,
                Email      = email,
                FirstName  = "E2E",
                LastName   = lastName,
            });

            if (response.IsSuccessStatusCode) return;

            body = await response.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(response!.IsSuccessStatusCode,
            $"Expected /api/dev/ensure-employee-login to succeed, got {response.StatusCode}. Response body: {body}");
    }

    [Fact]
    public async Task SelfServiceDocumentsTab_UploadRequestedDocument_CompletesTheRequest()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (employeeId, email, lastName) = CreateEmployeeAsync();

        await empAdmin.GoToAsync(AcmeId, employeeId);
        await empAdmin.OpenDocumentsTabAsync();
        await empAdmin.RequestDocumentAsync("Certificate");

        await EnsureEmployeeLoginAsync(employeeId, email, lastName);

        await login.LoginAsync(email);
        await profile.GoToAsync(AcmeId, employeeId);
        await profile.OpenDocumentsTabAsync();

        await _page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.True(await profile.HasUploadButtonForDocumentRequestAsync("Certificate"),
            "Expected an 'Upload' button on the fresh employee's outstanding Certificate request row");

        var tempFile = Path.Combine(Path.GetTempPath(), $"certificate-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await profile.UploadRequestedDocumentAsync("Certificate", tempFile);

            Assert.False(await profile.HasUploadButtonForDocumentRequestAsync("Certificate"),
                "Expected the Certificate request's 'Upload' button to disappear once the document has been uploaded");
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

        var downloadBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Download" }).First;
        Assert.True(await downloadBtn.IsVisibleAsync(),
            "Expected a Download button to be visible for Tom's seeded documents");

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
