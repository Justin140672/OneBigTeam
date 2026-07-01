using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetAssetEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000098");

    public GetAssetEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
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
    public async Task Get_Asset_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Asset_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Asset_Returns_Asset_When_Found()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);
        var asset = await CreateAssetAsync(client, companyId, category.Id, "ASSET-GET-001", "Laptop Pro");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{asset.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssetPayload>();
        Assert.NotNull(payload);
        Assert.Equal(asset.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("ASSET-GET-001", payload.AssetNumber);
        Assert.Equal(category.Id, payload.CategoryId);
        Assert.Equal("Laptop Pro", payload.Name);
        Assert.Equal("Available", payload.Status);
    }

    [Fact]
    public async Task Get_Asset_Returns_All_Optional_Fields_When_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var category = await CreateCategoryAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber  = "OPT-001",
            categoryId   = category.Id,
            name         = "ThinkPad X1",
            manufacturer = "Lenovo",
            model        = "ThinkPad X1 Carbon Gen 12",
            serialNumber = "SN-ABC-123",
            purchaseDate = "2024-06-01",
            purchasePrice = 1499.99m
        });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<AssetPayload>();

        var getResp = await client.GetAsync($"/api/companies/{companyId}/assets/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var payload = await getResp.Content.ReadFromJsonAsync<AssetPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Lenovo",                       payload!.Manufacturer);
        Assert.Equal("ThinkPad X1 Carbon Gen 12",   payload.Model);
        Assert.Equal("SN-ABC-123",                   payload.SerialNumber);
        Assert.Equal(new DateOnly(2024, 6, 1),       payload.PurchaseDate);
        Assert.Equal(1499.99m,                       payload.PurchasePrice);
    }

    [Fact]
    public async Task Get_Asset_Returns_NotFound_When_Asset_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        using var otherClient = AdminClient(otherCompanyId);

        var category = await CreateCategoryAsync(client, companyId);
        var asset = await CreateAssetAsync(client, companyId, category.Id);

        var response = await otherClient.GetAsync($"/api/companies/{otherCompanyId}/assets/{asset.Id}");

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
