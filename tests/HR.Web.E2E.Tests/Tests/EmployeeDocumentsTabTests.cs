using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that the admin employee Documents tab shows the seeded documents
/// and that HR can see document titles in the grid.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeDocumentsTabTests(AppFixture fixture) : E2ETestBase(fixture)
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
    public async Task Working_Pattern_Override_Can_Be_Set_Via_Admin_Profile()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura ────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Tom's admin profile ───────────────────────────
        await empAdmin.GoToAsync(AcmeId, TomId);

        // ── Step 3: Enable the working pattern override ───────────────────────
        await empAdmin.EnableWorkingPatternOverrideAsync();

        // ── Step 4: Set hours per day ─────────────────────────────────────────
        await empAdmin.SetHoursPerDayAsync(7m);

        // ── Step 5: Save — page navigates to employee list on success ─────────
        await empAdmin.SaveAsync();

        var content = await _page.ContentAsync();
        Assert.DoesNotContain("alert-danger", content);
    }
}
