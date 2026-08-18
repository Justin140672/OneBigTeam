using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an HR Administrator can assign a manager to an employee
/// via the Employment tab on the admin employee profile.
/// </summary>
public sealed class AssignManagerTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    // Marcus Diallo — seeded HR Advisor with no manager set, suitable as the target for this test.
    private static readonly Guid MarcusId = Guid.Parse("30000000-0000-0000-0000-000000000006");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AssignManager_SavesSuccessfully_AndReflectsOnAdminProfile()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura ─────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Marcus's admin employee profile ───────────────
        await empEdit.GoToAsync(AcmeId, MarcusId);

        // ── Step 3: Open the Employment tab ──────────────────────────────────
        await empEdit.OpenEmploymentTabAsync();

        // ── Step 4: Select Laura Bennett as Marcus's manager ──────────────────
        await empEdit.SelectManagerAsync("Laura Bennett");

        // ── Step 5: Save the employment details ──────────────────────────────
        await empEdit.ClickSaveChangesAsync();

        // ── Step 6: Verify no error was shown ────────────────────────────────
        Assert.False(await empEdit.HasErrorAsync(),
            "Expected no error after assigning a manager");

        // ── Step 7: Reload and verify the manager persisted ──────────────────
        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();

        var content = await _page.ContentAsync();
        Assert.Contains("Laura Bennett", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearManager_ViaNoManagerOption_PersistsAsUnset()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura ─────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Marcus's admin employee profile and set a manager ──
        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        await empEdit.SelectManagerAsync("Laura Bennett");
        await empEdit.ClickSaveChangesAsync();
        Assert.False(await empEdit.HasErrorAsync(), "Expected no error after assigning a manager");

        // ── Step 3: Reload, then clear the manager via the "No Manager" sentinel item ──
        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.Equal("Laura Bennett", await empEdit.GetSelectedManagerTextAsync());

        await empEdit.ClearManagerAsync();
        await empEdit.ClickSaveChangesAsync();
        Assert.False(await empEdit.HasErrorAsync(), "Expected no error after clearing the manager");

        // ── Step 4: Reload and verify the cleared state persisted ─────────────
        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.Equal("No Manager", await empEdit.GetSelectedManagerTextAsync());
    }
}
