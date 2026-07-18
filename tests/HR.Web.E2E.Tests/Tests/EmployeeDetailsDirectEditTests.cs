using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an HR Administrator can edit an employee's own Details tab
/// (EmployeeEdit.razor's "Personal Information" section) directly and have it persist — distinct
/// from the self-service "request a change" workflow covered by PersonalDetailsTabTests /
/// PersonalDetailsChangeRequestTests, which routes an employee's own edits through an approval
/// step instead of saving immediately. HR administrators use the single page-level Save button
/// (EmployeeEditPage.ClickSaveChangesAsync), which writes straight through — no request/approval
/// involved.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeDetailsDirectEditTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId        = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid JamesOkaforId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DetailsTab_DirectEditByHrAdmin_PersistsAfterReload()
    {
        var unique          = Guid.NewGuid().ToString("N")[..8];
        var preferredName   = $"E2E Preferred {unique}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HR Administrator) ──────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Open James Okafor's Details tab directly (default tab) ─────
        await empEdit.GoToAsync(AcmeId, JamesOkaforId);

        // ── Step 3: Change the Preferred Name field directly (no change-request flow) ──
        await _page.GetByPlaceholder("Defaults to first name").FillAsync(preferredName);

        // ── Step 4: Save via the single page-level Save button ─────────────────
        await empEdit.ClickSaveChangesAsync();

        // ── Step 5: Reload the page and verify the change persisted ────────────
        await empEdit.GoToAsync(AcmeId, JamesOkaforId);

        var value = await _page.GetByPlaceholder("Defaults to first name").InputValueAsync();
        Assert.Equal(preferredName, value);
    }
}
