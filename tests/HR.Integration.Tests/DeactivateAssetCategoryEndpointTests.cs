using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeactivateAssetCategoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000099");

    public DeactivateAssetCategoryEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Delete_AssetCategory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/asset-categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetCategory_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/asset-categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetCategory_Returns_NoContent_On_Success()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Electronics"
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        Assert.NotNull(category);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/asset-categories/{category!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetCategory_Deactivates_The_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Furniture"
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        Assert.NotNull(category);
        Assert.True(category!.IsActive);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/asset-categories/{category.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/asset-categories");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<List<AssetCategoryPayload>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list!, c => c.Id == category.Id);
    }

    [Fact]
    public async Task Delete_AssetCategory_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var createClient = AdminClient(companyId);

        var created = await createClient.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Vehicles"
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        Assert.NotNull(category);

        using var otherClient = AdminClient(otherCompanyId);
        var response = await otherClient.DeleteAsync(
            $"/api/companies/{otherCompanyId}/asset-categories/{category!.Id}");

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

}
