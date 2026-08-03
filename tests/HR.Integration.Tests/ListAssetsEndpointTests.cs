using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListAssetsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000010-0000-0000-0000-000000000099");

    public ListAssetsEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name = "Electronics")
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

    private async Task<AssetPayload> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId, string assetNumber, string name)
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
    public async Task Get_Assets_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Assets_Returns_Empty_List_When_No_Assets_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task Get_Assets_Returns_All_Assets_For_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategoryAsync(client, companyId);

        await CreateAssetAsync(client, companyId, categoryId, "A001", "Laptop");
        await CreateAssetAsync(client, companyId, categoryId, "A002", "Desk");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetPayload>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);
        Assert.All(payload, p => Assert.Equal(companyId, p.CompanyId));
    }

    [Fact]
    public async Task Get_Assets_Returns_Assets_Ordered_By_AssetNumber()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategoryAsync(client, companyId);

        await CreateAssetAsync(client, companyId, categoryId, "C003", "Monitor");
        await CreateAssetAsync(client, companyId, categoryId, "A001", "Laptop");
        await CreateAssetAsync(client, companyId, categoryId, "B002", "Desk");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetPayload>>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Count);
        Assert.Equal("A001", payload[0].AssetNumber);
        Assert.Equal("B002", payload[1].AssetNumber);
        Assert.Equal("C003", payload[2].AssetNumber);
    }

    [Fact]
    public async Task Get_Assets_Does_Not_Return_Assets_From_Other_Companies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        using var otherClient = AdminClient(otherCompanyId);

        var otherCategoryId = await CreateCategoryAsync(otherClient, otherCompanyId);
        await CreateAssetAsync(otherClient, otherCompanyId, otherCategoryId, "A001", "Laptop");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task Get_Assets_Filters_By_Status_When_Status_Query_Parameter_Provided()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategoryAsync(client, companyId);

        var asset = await CreateAssetAsync(client, companyId, categoryId, "A001", "Laptop");
        await CreateAssetAsync(client, companyId, categoryId, "A002", "Desk");

        // Assign the first asset
        await client.PostAsJsonAsync($"/api/companies/{companyId}/assets/{asset.Id}/assignments", new
        {
            companyId,
            assetId = asset.Id,
            employeeId = Guid.NewGuid(),
            assignedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/assets?status=Available");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetPayload>>();
        Assert.NotNull(payload);
        Assert.All(payload!, p => Assert.Equal("Available", p.Status));
    }

    [Fact]
    public async Task Get_Assets_Returns_Empty_List_When_No_Assets_Match_Status_Filter()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategoryAsync(client, companyId);

        await CreateAssetAsync(client, companyId, categoryId, "A001", "Laptop");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets?status=Retired");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    private sealed record AssetCategoryPayload(Guid Id, Guid CompanyId, string Name, string? Description, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

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
