using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Assignments section on the asset detail page.
///
/// Uses seeded data:
///   - ASSET-0001 (MacBook Pro 14") assigned to Tom Williams — ID c0000000-0000-0000-0000-000000000002
///     with assignment c0000000-0000-0000-0000-000000000003
///   - ASSET-0002 (Dell UltraSharp 27") assigned to Sarah Chen — ID c0000000-0000-0000-0000-000000000004
///     with assignment c0000000-0000-0000-0000-000000000005
///
/// Both assets belong to company 00000000-0000-0000-0000-000000000001 (Acme Corp).
/// Both assets are in Assigned status, so the "Assign to Employee" button is not shown.
///
/// Admin user: Laura Bennett (laura.bennett@acme.example) who holds the
/// asset:view permission via the HrAdministrator role.
/// </summary>
[Collection("E2E")]
public sealed class AssetAssignmentsSectionTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomAssetId  = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid SarahAssetId = Guid.Parse("c0000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task AssetDetail_ShowsAssignmentsSection()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);
        await detail.WaitForAssignmentsSectionAsync();

        Assert.True(await detail.IsAssignmentsSectionVisibleAsync(),
            "The Assignments section should be visible on the asset detail page");
    }

    [Fact]
    public async Task AssetDetail_AssignmentsSection_ShowsGridWithRows_ForAssignedAsset()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);
        await detail.WaitForAssignmentsSectionAsync();

        Assert.True(await detail.HasAssignmentsGridRowsAsync(),
            "Expected the assignments grid to have at least one row for ASSET-0001 (Tom's MacBook)");
    }

    [Fact]
    public async Task AssetDetail_AssignmentsSection_ShowsGridWithRows_ForSarahAsset()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await detail.GoToAsync(AcmeId, SarahAssetId);
        await detail.WaitForAssignmentsSectionAsync();

        Assert.True(await detail.HasAssignmentsGridRowsAsync(),
            "Expected the assignments grid to have at least one row for ASSET-0002 (Sarah's monitor)");
    }

    [Fact]
    public async Task AssetDetail_AssignToEmployeeButton_NotVisible_WhenAssetIsAssigned()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);
        await detail.WaitForAssignmentsSectionAsync();

        // ASSET-0001 is in "Assigned" status, so the button should not appear
        Assert.False(await detail.IsAssignToEmployeeButtonVisibleAsync(),
            "The 'Assign to Employee' button should not be visible when the asset is already assigned");
    }

    [Fact]
    public async Task AssetDetail_AssignmentsSection_IsAccessibleByEmployee()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);
        await detail.WaitForAssignmentsSectionAsync();

        Assert.True(await detail.IsAssignmentsSectionVisibleAsync(),
            "The Assignments section should be visible for employees viewing their own asset");

        Assert.True(await detail.HasAssignmentsGridRowsAsync(),
            "Expected at least one assignment row for Tom's MacBook");
    }

    [Fact]
    public async Task AssetDetail_RequestReturnButton_IsVisible_WhenAssetIsAssigned()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);
        await detail.WaitForAssignmentsSectionAsync();

        Assert.True(await detail.IsRequestReturnButtonVisibleAsync(),
            "The 'Request Return' button should be visible when the asset is in Assigned status");
    }

}
