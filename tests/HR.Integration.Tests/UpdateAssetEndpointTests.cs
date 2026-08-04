using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateAssetEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000097");

    public UpdateAssetEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<AssetCategoryPayload> CreateCategoryAsync(HttpClient client, Guid companyId, string name = "Electronics")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetCategoryPayload>())!;
    }

    private async Task<AssetPayload> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId, string assetNumber = "ASSET-001", string name = "Laptop")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber,
            categoryId,
            name
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetPayload>())!;
    }

    [Fact]
    public async Task Put_Asset_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/assets/{Guid.NewGuid()}",
            new { assetNumber = "ASSET-001", name = "Laptop" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Asset_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{Guid.NewGuid()}",
            new
            {
                companyId,
                id = Guid.NewGuid(),
                assetNumber = "ASSET-001",
                categoryId = category.Id,
                name = "Ghost"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Asset_Updates_All_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);
        var asset = await CreateAssetAsync(client, companyId, category.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset.Id}",
            new
            {
                companyId,
                id = asset.Id,
                assetNumber = "ASSET-UPDATED",
                categoryId = category.Id,
                name = "Updated Laptop",
                manufacturer = "Dell",
                model = "XPS 15",
                serialNumber = "SN999",
                purchaseDate = "2025-03-01",
                purchasePrice = 2000.00m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssetPayload>();
        Assert.NotNull(payload);
        Assert.Equal(asset.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("ASSET-UPDATED", payload.AssetNumber);
        Assert.Equal(category.Id, payload.CategoryId);
        Assert.Equal("Updated Laptop", payload.Name);
        Assert.Equal("Dell", payload.Manufacturer);
        Assert.Equal("XPS 15", payload.Model);
        Assert.Equal("SN999", payload.SerialNumber);
        Assert.Equal(2000.00m, payload.PurchasePrice);
    }

    [Fact]
    public async Task Put_Asset_Returns_UnprocessableEntity_When_Name_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);
        var asset = await CreateAssetAsync(client, companyId, category.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset.Id}",
            new
            {
                companyId,
                id = asset.Id,
                assetNumber = "ASSET-001",
                categoryId = category.Id,
                name = string.Empty
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Asset_Returns_UnprocessableEntity_When_AssetNumber_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);
        var asset = await CreateAssetAsync(client, companyId, category.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset.Id}",
            new
            {
                companyId,
                id = asset.Id,
                assetNumber = string.Empty,
                categoryId = category.Id,
                name = "Laptop"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Asset_Returns_Conflict_When_AssetNumber_Belongs_To_Another_Asset()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);
        var asset1 = await CreateAssetAsync(client, companyId, category.Id, "ASSET-001", "Laptop");
        await CreateAssetAsync(client, companyId, category.Id, "ASSET-002", "Monitor");

        // Try to update asset1 to use ASSET-002's number
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset1.Id}",
            new
            {
                companyId,
                id = asset1.Id,
                assetNumber = "ASSET-002",
                categoryId = category.Id,
                name = "Laptop"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Asset_Returns_NotFound_When_Asset_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        using var otherClient = await AdminClient(otherCompanyId);

        var category = await CreateCategoryAsync(client, companyId);
        var asset = await CreateAssetAsync(client, companyId, category.Id);

        var otherCategory = await CreateCategoryAsync(otherClient, otherCompanyId);

        // Try to update asset from other company's context
        var response = await otherClient.PutAsJsonAsync(
            $"/api/companies/{otherCompanyId}/assets/{asset.Id}",
            new
            {
                companyId = otherCompanyId,
                id = asset.Id,
                assetNumber = "ASSET-001",
                categoryId = otherCategory.Id,
                name = "Laptop"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record AssetCategoryPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record AssetPayload(
        Guid Id,
        Guid CompanyId,
        string AssetNumber,
        Guid CategoryId,
        string Name,
        string? Manufacturer,
        string? Model,
        string? SerialNumber,
        DateOnly? PurchaseDate,
        decimal? PurchasePrice,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
