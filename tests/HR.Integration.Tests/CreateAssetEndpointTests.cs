using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateAssetEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000003-0000-0000-0000-000000000099");

    public CreateAssetEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> CreateActiveCategoryAsync(HttpClient client, Guid companyId, string name = "Electronics")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_Assets_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/assets", new
        {
            assetNumber = "ASSET-001",
            categoryId = Guid.NewGuid(),
            name = "Laptop"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Assets_Creates_Asset_With_All_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = "ASSET-001",
            categoryId,
            name = "Laptop",
            manufacturer = "Dell",
            model = "XPS 15",
            serialNumber = "SN123456",
            purchaseDate = "2024-01-15",
            purchasePrice = 1500.00m
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<AssetPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("ASSET-001", payload.AssetNumber);
        Assert.Equal(categoryId, payload.CategoryId);
        Assert.Equal("Laptop", payload.Name);
        Assert.Equal("Dell", payload.Manufacturer);
        Assert.Equal("XPS 15", payload.Model);
        Assert.Equal("SN123456", payload.SerialNumber);
        Assert.Equal(1500.00m, payload.PurchasePrice);
        Assert.Equal("Available", payload.Status);
    }

    [Fact]
    public async Task Post_Assets_Creates_Asset_With_Minimal_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId, "Furniture");

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = "CHAIR-001",
            categoryId,
            name = "Office Chair"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssetPayload>();
        Assert.NotNull(payload);
        Assert.Equal("CHAIR-001", payload!.AssetNumber);
        Assert.Equal("Office Chair", payload.Name);
        Assert.Null(payload.Manufacturer);
        Assert.Null(payload.Model);
        Assert.Null(payload.SerialNumber);
        Assert.Null(payload.PurchaseDate);
        Assert.Null(payload.PurchasePrice);
        Assert.Equal("Available", payload.Status);
    }

    [Fact]
    public async Task Post_Assets_Returns_UnprocessableEntity_When_AssetNumber_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = string.Empty,
            categoryId,
            name = "Laptop"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Assets_Returns_UnprocessableEntity_When_Name_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = "ASSET-X",
            categoryId,
            name = string.Empty
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Assets_Returns_Conflict_When_AssetNumber_Already_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId);

        var body = new
        {
            companyId,
            assetNumber = "DUPLICATE-001",
            categoryId,
            name = "First Laptop"
        };

        var firstResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", body);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = "DUPLICATE-001",
            categoryId,
            name = "Second Laptop"
        });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Post_Assets_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = "ASSET-002",
            categoryId = Guid.NewGuid(),
            name = "Laptop"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Assets_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        var companyId1 = Guid.NewGuid();
        var companyId2 = Guid.NewGuid();
        using var client1 = await AdminClient(companyId1);
        using var client2 = await AdminClient(companyId2);

        var categoryId = await CreateActiveCategoryAsync(client1, companyId1);

        var response = await client2.PostAsJsonAsync($"/api/companies/{companyId2}/assets", new
        {
            companyId = companyId2,
            assetNumber = "ASSET-003",
            categoryId,
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
