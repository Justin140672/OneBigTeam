using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator active/inactive filtering workflow for document types:
/// - Deactivate a document type.
/// - It disappears from the default (active-only) list view.
/// - Toggling "Show Inactive" reveals it again.
/// </summary>
public sealed class DocumentTypeManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DeactivateDocumentType_HidesFromActiveList_ShowsWhenInactiveToggled()
    {
        var typeName = $"E2E Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new DocumentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new DocumentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create first.
        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.SaveAsync();

        // Deactivate.
        await typeList.GoToAsync(AcmeId);
        Assert.True(await typeList.IsActiveAsync(typeName), "Expected newly created document type to be Active");
        await typeList.DeactivateAsync(typeName);

        Assert.False(await typeList.HasItemAsync(typeName),
            $"Expected '{typeName}' to no longer appear in the default active-only view after deactivation");

        // Show inactive and verify it reappears.
        await typeList.ShowInactiveAsync();

        Assert.True(await typeList.HasItemAsync(typeName),
            "Expected deactivated document type to appear when 'Show Inactive' is enabled");
    }
}
