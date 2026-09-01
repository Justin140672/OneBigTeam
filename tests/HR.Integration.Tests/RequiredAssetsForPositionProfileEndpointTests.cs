using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Integration coverage for the position-profile required-asset slices:
/// AddRequiredAssetToPositionProfile, RemoveRequiredAssetFromPositionProfile and
/// ListRequiredAssetsForPositionProfile. Real HTTP + EF/Postgres + real auth.
/// </summary>
[Collection("Integration")]
public class RequiredAssetsForPositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("cafe0001-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cafe0001-0000-0000-0000-000000000002");

    public RequiredAssetsForPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> CreateAssetCategoryAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = $"Cat-{Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_RequiredAsset_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/required-assets",
            new { assetCategoryId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredAsset_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, PlainEmployeeUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, PlainEmployeeUser, SystemRoles.Employee, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/required-assets",
            new { companyId, positionProfileId = Guid.NewGuid(), assetCategoryId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_RequiredAssets_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/required-assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_RequiredAsset_Creates_And_Persists_Entry()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var assetCategoryId = await CreateAssetCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new
            {
                companyId,
                positionProfileId = refData.PositionProfileId,
                assetCategoryId,
                isMandatory = true,
                quantity = 2
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RequiredAssetPayload>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.Equal(assetCategoryId, created.AssetCategoryId);
        Assert.True(created.IsMandatory);
        Assert.Equal(2, created.Quantity);

        // Persisted — visible through the list endpoint
        var list = await client.GetFromJsonAsync<RequiredAssetListPayload>(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets");
        var item = Assert.Single(list!.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.False(string.IsNullOrWhiteSpace(item.AssetCategoryName));
    }

    [Fact]
    public async Task Post_RequiredAsset_Returns_NotFound_For_Unknown_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var assetCategoryId = await CreateAssetCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/required-assets",
            new { companyId, positionProfileId = Guid.NewGuid(), assetCategoryId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredAsset_Returns_NotFound_For_Unknown_AssetCategory()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredAsset_Returns_NotFound_When_AssetCategory_Belongs_To_Another_Company()
    {
        // Category is created under a different company — the reader is company-scoped, so it must
        // not be visible to this position profile.
        var otherCompanyId = Guid.NewGuid();
        using var otherClient = await AdminClient(otherCompanyId);
        var foreignCategoryId = await CreateAssetCategoryAsync(otherClient, otherCompanyId);

        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId = foreignCategoryId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredAsset_Returns_Conflict_For_Duplicate_Active_AssetCategory()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var assetCategoryId = await CreateAssetCategoryAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredAsset_Returns_422_For_Quantity_Below_One()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var assetCategoryId = await CreateAssetCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId, quantity = 0 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RequiredAsset_Removes_Entry_And_Allows_ReAdd()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var assetCategoryId = await CreateAssetCategoryAsync(client, companyId);

        var addResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId });
        var added = await addResponse.Content.ReadFromJsonAsync<RequiredAssetPayload>();

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets/{added!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var list = await client.GetFromJsonAsync<RequiredAssetListPayload>(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets");
        Assert.Empty(list!.Items);

        // Deactivation (not hard delete) must not block re-adding the same category.
        var reAdd = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId });
        Assert.Equal(HttpStatusCode.Created, reAdd.StatusCode);
    }

    [Fact]
    public async Task Delete_RequiredAsset_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RequiredAsset_Returns_NotFound_When_Already_Removed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var assetCategoryId = await CreateAssetCategoryAsync(client, companyId);

        var added = await (await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets",
            new { companyId, positionProfileId = refData.PositionProfileId, assetCategoryId }))
            .Content.ReadFromJsonAsync<RequiredAssetPayload>();

        var firstDelete = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets/{added!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        var secondDelete = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets/{added.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_RequiredAssets_Returns_NotFound_For_Unknown_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/required-assets");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_RequiredAssets_Returns_Empty_List_For_Profile_With_No_Required_Assets()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var list = await client.GetFromJsonAsync<RequiredAssetListPayload>(
            $"/api/companies/{companyId}/position-profiles/{refData.PositionProfileId}/required-assets");

        Assert.NotNull(list);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Get_RequiredAssets_Returns_Forbidden_When_Route_Company_Does_Not_Match_Tenant()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{refData.PositionProfileId}/required-assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record RequiredAssetPayload(Guid Id, Guid PositionProfileId, Guid AssetCategoryId, bool IsMandatory, int Quantity);
    private sealed record RequiredAssetListItemPayload(Guid Id, Guid AssetCategoryId, string AssetCategoryName, bool IsMandatory, int Quantity);
    private sealed record RequiredAssetListPayload(List<RequiredAssetListItemPayload> Items);
}
