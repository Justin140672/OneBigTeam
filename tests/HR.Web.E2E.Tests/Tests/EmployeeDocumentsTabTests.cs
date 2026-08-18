using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that the admin employee Documents tab shows the seeded documents
/// and that HR can see document titles in the grid.
/// </summary>
public sealed class EmployeeDocumentsTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Admin_Documents_Tab_Shows_Seeded_Employee_Documents()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HR Manager with employee:manage access) ───
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Tom's admin profile ───────────────────────────
        await empAdmin.GoToAsync(AcmeId, TomId);

        // ── Step 3: Open the Documents tab ───────────────────────────────────
        await empAdmin.OpenDocumentsTabAsync();

        // ── Step 4: Verify seeded documents are in the grid ──────────────────
        Assert.True(await empAdmin.HasDocumentAsync("Employment Contract"),
            "Expected 'Employment Contract – Tom Williams' to appear in the Documents grid");

        Assert.True(await empAdmin.HasDocumentAsync("Offer Letter"),
            "Expected 'Offer Letter – Tom Williams' to appear in the Documents grid");
    }

    [Fact]
    public async Task Admin_Documents_Tab_Shows_Document_Requests_Section()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        Assert.True(await empAdmin.HasDocumentRequestsSectionAsync(),
            "Expected the Document Requests section to be visible on Tom's Documents tab");

        Assert.True(await empAdmin.HasDocumentRequestAsync("Passport"),
            "Expected Tom's seeded Passport document request to appear in the Document Requests section");
    }

    [Fact]
    public async Task Admin_Documents_Tab_Shows_Requested_Status_Badge_For_Outstanding_Request()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        var status = await empAdmin.GetDocumentRequestStatusAsync("Passport");
        Assert.Equal("Requested", status);
    }

    [Fact]
    public async Task Admin_Documents_Tab_Shows_Request_Document_Button()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        Assert.True(await empAdmin.HasRequestDocumentButtonAsync(),
            "Expected a 'Request Document' button to be visible on the admin Documents tab");
    }

    [Fact]
    public async Task Request_Document_Dialog_Creates_New_Document_Request()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        // Request a document type that doesn't already have an open request for Tom
        await empAdmin.RequestDocumentAsync("Driving Licence");

        // After the dialog closes the tab reloads — the new request should appear
        Assert.True(await empAdmin.HasDocumentRequestAsync("Driving Licence"),
            "Expected the newly requested 'Driving Licence' to appear in the Document Requests section");
    }

    [Fact]
    public async Task Request_Document_Dialog_Cancel_Works_After_Selecting_A_Document_Type()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenDocumentsTabAsync();

        // Selecting a document type marks the dialog's form "dirty", which used to make the
        // Cancel button silently do nothing (see EmployeeAdminPage method doc for why). Uses
        // "Right To Work" rather than "Driving Licence" (used by the request-creation test above)
        // to avoid a false pass/fail if these tests run against the same shared Tom Williams row.
        await empAdmin.OpenRequestDocumentDialogSelectTypeThenCancelAsync("Right To Work");

        // ".request-document-dialog" matches multiple elements (the SfDialog container, the
        // role="dialog" element itself, and its close-icon button all carry this CssClass), so an
        // unscoped IsVisibleAsync() throws a Playwright strict-mode violation rather than
        // resolving. .First mirrors EmployeeAdminPage's own scoping convention for this selector.
        Assert.False(await _page.Locator(".request-document-dialog").First.IsVisibleAsync(),
            "Expected the Request Document dialog to actually close after Cancel + Discard Changes");

        // No request should have been created for the cancelled attempt.
        Assert.False(await empAdmin.HasDocumentRequestAsync("Right To Work"),
            "Expected cancelling the dialog to not create a 'Right To Work' document request");
    }

    [Fact]
    public async Task Working_Pattern_Override_Can_Be_Set_Via_Admin_Profile()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura ────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Tom's admin profile ───────────────────────────
        await empAdmin.GoToAsync(AcmeId, TomId);

        // ── Step 3: Open the Employment tab and enable the working pattern override ──
        await empAdmin.OpenEmploymentTabAsync();
        await empAdmin.EnableWorkingPatternOverrideAsync();

        // ── Step 4: Set hours per day ─────────────────────────────────────────
        await empAdmin.SetHoursPerDayAsync(7m);

        // ── Step 5: Save — page navigates to employee list on success ─────────
        await empAdmin.SaveAsync();

        var content = await _page.ContentAsync();
        Assert.DoesNotContain("alert-danger", content);
    }
}
