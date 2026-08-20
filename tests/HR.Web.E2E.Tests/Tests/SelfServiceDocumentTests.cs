using System.Net.Http.Json;
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
public sealed class SelfServiceDocumentTests(EmployeePersonaFixture fixture) : SupabaseAuthSerialEmployeeTestBase(fixture)
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
        // MyProfileDocumentsTab.razor (the merged Documents tab — see item 34) always renders its
        // own bulk "Upload" button (self-service employees can now upload a document of their own
        // from My Profile, not only in response to an HR request) — this replaced the older
        // EmployeeDocumentsTab.razor-based tab, whose bulk Upload button was admin-only and never
        // rendered for EmployeeSelfUpload. Each "Requested" row in the Document Requests table
        // still gets its own contextual "Upload" button too — Tom has exactly one open request
        // (Passport) — so 2 "Upload" buttons should be visible in total: 1 bulk + 1 per-request.
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
        Assert.Equal(2, await uploadBtns.CountAsync());
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
        // "Document Requests" section on MyProfileDocumentsTab.razor (data-testid=
        // "my-profile-document-requests-section" — distinct from EmployeeDocumentsTab.razor's
        // admin equivalent, "admin-document-requests-section") is now the only place this shows.
        var requestedSection = _page.Locator("[data-testid='my-profile-document-requests-section']");
        Assert.True(await requestedSection.IsVisibleAsync(),
            "Expected the 'Document Requests' section to be visible for Tom, who has a Passport request");

        var content = await requestedSection.TextContentAsync();
        Assert.Contains("Passport", content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form and returns their id and
    /// work email, captured from the URL after navigating back into their profile from the
    /// employee list. Caller must already be logged in as an HR administrator.
    /// </summary>
    private async Task<(Guid EmployeeId, string Email, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"SelfDoc{unique}";
        var workEmail = $"e2e.selfdoc.{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = System.Text.RegularExpressions.Regex.Match(_page.Url,
            @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return (Guid.Parse(match.Groups[1].Value), workEmail, lastName);
    }

    /// <summary>
    /// Gives a freshly-created employee a real, working Supabase login via the dev-only
    /// POST /api/dev/ensure-employee-login endpoint — see AssetAcknowledgementTaskTests'
    /// EnsureEmployeeLoginAsync for the full rationale (a brand-new Employee row has no linked
    /// UserProfile by construction). 404s outside Development.
    /// </summary>
    private async Task EnsureEmployeeLoginAsync(Guid employeeId, string email, string lastName)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        // This endpoint's EnsureDevSupabaseUserAsync makes a real, network-dependent call to
        // Supabase's Admin API (unlike most other E2E auth paths, which are faked under
        // E2E_TESTING=true — see FakeSupabaseAuthGateway's own remarks) — a genuine transient
        // failure/rate-limit response under this suite's concurrency can surface as a 500 here.
        // Retry a couple of times before failing outright, and capture the response body on
        // failure so a real, non-transient error is immediately diagnosable.
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
        // This used to upload against Tom Williams' seeded Passport request directly. That's
        // irreversible (no "un-upload" action) and permanently flips the seeded request from
        // "Requested" to "Uploaded" on the shared, long-lived E2E dev database — colliding with
        // every other read-only test in this file and in EmployeeDocumentsTabTests that expects
        // Tom's Passport request to still show "Requested"
        // (Admin_Documents_Tab_Shows_Requested_Status_Badge_For_Outstanding_Request in particular).
        // Same fix pattern as AssetAcknowledgementTaskTests/AssetReturnTaskTests: use a fresh
        // employee (with a fresh "Certificate" document request) instead of Tom.
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (employeeId, email, lastName) = await CreateEmployeeAsync(empList, empEdit);

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

            // The grid reloads after a successful upload — the request should no longer show an
            // "Upload" action (its status has moved on from "Requested").
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
