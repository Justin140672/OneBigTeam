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
///
/// Edits a freshly API-created employee of its own, NOT a seeded persona: this test previously
/// renamed James Okafor's Preferred Name to "E2E Preferred {guid}" and never restored it, which
/// permanently changed how every other test sees him (e.g. EmployeeDirectoryTests' "James Okafor"
/// directory card became "E2E Preferred … Okafor" and was never found). Restoring in a finally
/// wouldn't be enough either — tests reading James concurrently would still observe the
/// transient name mid-test.
/// </summary>
public sealed class EmployeeDetailsDirectEditTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DetailsTab_DirectEditByHrAdmin_PersistsAfterReload()
    {
        var unique          = Guid.NewGuid().ToString("N")[..8];
        var preferredName   = $"E2E Preferred {unique}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // Arrange: this test's own employee (see class remarks).
        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "DirectEdit");

        // ── Step 1: Login as Laura (HR Administrator) ──────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Open the employee's Details tab directly (default tab) ─────
        await empEdit.GoToAsync(AcmeId, employee.Id);

        // ── Step 3: Change the Preferred Name field directly (no change-request flow) ──
        await _page.GetByPlaceholder("Defaults to first name").FillAsync(preferredName);

        // ── Step 4: Save via the single page-level Save button ─────────────────
        await empEdit.ClickSaveChangesAsync();

        // ── Step 5: Reload the page and verify the change persisted ────────────
        await empEdit.GoToAsync(AcmeId, employee.Id);

        var value = await _page.GetByPlaceholder("Defaults to first name").InputValueAsync();
        Assert.Equal(preferredName, value);
    }
}
