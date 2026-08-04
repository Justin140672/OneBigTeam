using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateAssetCategoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000098");

    public UpdateAssetCategoryEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_AssetCategory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/asset-categories/{Guid.NewGuid()}",
            new { name = "Electronics" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssetCategory_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), name = "Electronics" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssetCategory_Updates_Name_And_Description()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Electronics"
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        Assert.NotNull(category);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{category!.Id}",
            new
            {
                companyId,
                id = category.Id,
                name = "Consumer Electronics",
                description = "Phones, laptops, and accessories"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(category.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Consumer Electronics", payload.Name);
        Assert.Equal("Phones, laptops, and accessories", payload.Description);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Put_AssetCategory_Returns_UnprocessableEntity_When_Name_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Furniture"
        });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<AssetCategoryPayload>();
        Assert.NotNull(category);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{category!.Id}",
            new { companyId, id = category.Id, name = string.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
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
