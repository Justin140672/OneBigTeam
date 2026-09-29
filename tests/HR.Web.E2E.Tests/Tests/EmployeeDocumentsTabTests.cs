using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

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

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);

        await empAdmin.OpenDocumentsTabAsync();

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

        await empAdmin.RequestDocumentAsync("Driving Licence");

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

        try
        {
            await _page.Locator(".request-document-dialog").First
                .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            Assert.Fail("Expected the Request Document dialog to actually close after Cancel + Discard Changes");
        }

        Assert.False(await empAdmin.HasDocumentRequestAsync("Right To Work"),
            "Expected cancelling the dialog to not create a 'Right To Work' document request");
    }

    [Fact]
    public async Task Working_Pattern_Override_Can_Be_Set_Via_Admin_Profile()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);

        await empAdmin.OpenEmploymentTabAsync();
        await empAdmin.EnableWorkingPatternOverrideAsync();

        await empAdmin.SetHoursPerDayAsync(7m);

        await empAdmin.SaveAsync();

        var content = await _page.ContentAsync();
        Assert.DoesNotContain("alert-danger", content);
    }
}
