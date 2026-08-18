using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Assets tab on the HR-admin employee edit page.
///
/// Uses seeded data:
///   - Tom Williams  (30000000-0000-0000-0000-000000000004) has a MacBook Pro 14"
///     assigned (ASSET-0001, assignment c0000000-0000-0000-0000-000000000003).
///   - Sarah Chen    (30000000-0000-0000-0000-000000000001) has a Dell UltraSharp 27"
///     assigned (ASSET-0002, assignment c0000000-0000-0000-0000-000000000005).
///   - Carlos Rivera (30000000-0000-0000-0000-000000000010) has no assigned assets.
///
/// Admin user: Laura Bennett (laura.bennett@acme.example) who holds the
/// employee:manage permission.
/// </summary>
public sealed class EmployeeAssetsTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid SarahId  = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AssetsTab_IsVisible_OnAdminEmployeeEditPage()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);

        // Auto-retrying assertion rather than a single IsVisibleAsync() snapshot — the Assets tab
        // item can render after GoToAsync's own wait condition (the Details tab's combobox) has
        // already resolved on an earlier render pass, same race class as Probation/Notes.
        var assetsTab = _page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Assets" });
        await Microsoft.Playwright.Assertions.Expect(assetsTab).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task AssetsTab_ShowsGrid_WithAssignedAssets_ForTom()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected the admin assets grid to contain at least one row for Tom who has a seeded asset");
    }

    [Fact]
    public async Task AssetsTab_ShowsCorrectAssetNumber_ForTom()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        var assetNumbers = await empAdmin.GetAssetsGridAssetNumbersAsync();
        Assert.Contains("ASSET-0001", assetNumbers);
    }

    [Fact]
    public async Task AssetsTab_ShowsGrid_WithAssignedAssets_ForSarah()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, SarahId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected the admin assets grid to contain at least one row for Sarah who has a seeded asset");

        var assetNumbers = await empAdmin.GetAssetsGridAssetNumbersAsync();
        Assert.Contains("ASSET-0002", assetNumbers);
    }

    [Fact]
    public async Task AssetsTab_ShowsEmptyGrid_ForEmployeeWithoutAssets()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, CarlosId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.False(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected no rows in the assets grid for Carlos who has no seeded assets");
    }

    [Fact]
    public async Task AssetsTab_ShowsAssignAssetButton()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasAssignAssetButtonAsync(),
            "Expected the 'Assign Asset' button to be visible on the admin Assets tab");
    }

    [Fact]
    public async Task AssetsTab_ShowsReturnAssetButton()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasReturnAssetButtonAsync(),
            "Expected the 'Return Asset' button to be visible on the admin Assets tab");
    }

    [Fact]
    public async Task AssetsTab_ReturnAssetButton_IsDisabled_WhenNoAssetsAssigned()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Carlos has no assigned assets, so there is nothing to return.
        await empAdmin.GoToAsync(AcmeId, CarlosId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.IsReturnAssetButtonDisabledAsync(),
            "Expected the 'Return Asset' button to be disabled when the employee has no assigned assets");
    }

    [Fact]
    public async Task AssetsTab_ClickingAssignAsset_OpensDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        await empAdmin.OpenAssignAssetDialogAsync();

        Assert.True(await empAdmin.IsAssignAssetDialogVisibleAsync(),
            "Expected the Assign Asset dialog to open after clicking the button");
    }

    [Fact]
    public async Task AssetsTab_AssignAssetDialog_CanBeDismissed()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        await empAdmin.OpenAssignAssetDialogAsync();
        Assert.True(await empAdmin.IsAssignAssetDialogVisibleAsync(),
            "Dialog should be open before dismissal");

        await empAdmin.CloseAssignAssetDialogAsync();
        Assert.False(await empAdmin.IsAssignAssetDialogVisibleAsync(),
            "Expected the Assign Asset dialog to close after clicking Cancel");
    }

    /// <summary>
    /// Creates a fresh, uniquely-named Acme employee (guaranteed to start with zero assigned
    /// assets) and returns their employee ID.
    ///
    /// AssetsTab_AssigningAvailableAsset_AddsRowToGrid used to assign the shared seeded ASSET-0003
    /// (Logitech MX Keys) to Carlos Rivera directly — an irreversible mutation (CreateAssetAssignmentHandler
    /// marks the asset Assigned; there is no unassign action in this UI), which permanently broke
    /// AssetsTab_ShowsEmptyGrid_ForEmployeeWithoutAssets and AssetsTab_ReturnAssetButton_IsDisabled_WhenNoAssetsAssigned
    /// in this same file, plus ProfileAssetsTabTests.AssetsTab_IsVisible_And_Shows_Empty_State_For_Employee_Without_Assets,
    /// all of which assert Carlos has zero assets. A freshly created employee has no assets by
    /// construction, so it's a safe target for this destructive assignment — it doesn't need any
    /// of the login-as-the-employee/acknowledgement machinery the asset-task tests need, since this
    /// test only checks the grid from the HR-admin side.
    /// </summary>
    private async Task<Guid> CreateFreshAssetlessEmployeeAsync()
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Asset{unique}";
        var workEmail = $"e2e.asset{unique}@acme.example";

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

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task AssetsTab_AssigningAvailableAsset_AddsRowToGrid()
    {
        // ASSET-0003 (Logitech MX Keys) is seeded as Available.
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var freshEmployeeId = await CreateFreshAssetlessEmployeeAsync();

        await empAdmin.GoToAsync(AcmeId, freshEmployeeId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.False(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected the freshly created employee to have no assets before assignment");

        await empAdmin.OpenAssignAssetDialogAsync();
        await empAdmin.SelectAssetAndConfirmAsync("ASSET-0003");

        Assert.True(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected the freshly created employee to have one asset row after assignment");

        var assetNumbers = await empAdmin.GetAssetsGridAssetNumbersAsync();
        Assert.Contains("ASSET-0003", assetNumbers);
    }
}
