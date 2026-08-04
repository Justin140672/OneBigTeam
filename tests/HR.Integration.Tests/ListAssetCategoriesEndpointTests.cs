using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListAssetCategoriesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000099");

    public ListAssetCategoriesEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Get_AssetCategories_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/asset-categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetCategories_Returns_Empty_List_When_No_Categories_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/asset-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetCategoryPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task Get_AssetCategories_Returns_Active_Categories_For_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Electronics",
            description = "Electronic devices"
        });

        await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Furniture"
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/asset-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetCategoryPayload>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);
        Assert.All(payload, p => Assert.Equal(companyId, p.CompanyId));
        Assert.All(payload, p => Assert.True(p.IsActive));
    }

    [Fact]
    public async Task Get_AssetCategories_Returns_Categories_Ordered_By_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new { companyId, name = "Vehicles" });
        await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new { companyId, name = "Computers" });
        await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new { companyId, name = "Furniture" });

        var response = await client.GetAsync($"/api/companies/{companyId}/asset-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetCategoryPayload>>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Count);
        Assert.Equal("Computers", payload[0].Name);
        Assert.Equal("Furniture", payload[1].Name);
        Assert.Equal("Vehicles", payload[2].Name);
    }

    [Fact]
    public async Task Get_AssetCategories_Does_Not_Return_Categories_From_Other_Companies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        using var otherClient = await AdminClient(otherCompanyId);

        await otherClient.PostAsJsonAsync($"/api/companies/{otherCompanyId}/asset-categories", new
        {
            companyId = otherCompanyId,
            name = "Other Company Category"
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/asset-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssetCategoryPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    private sealed record AssetCategoryPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
