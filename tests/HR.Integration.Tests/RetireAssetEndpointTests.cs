using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RetireAssetEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000009-0000-0000-0000-000000000099");

    public RetireAssetEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = $"ASSET-{Guid.NewGuid():N}",
            categoryId,
            name = "Laptop"
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssetPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Delete_Asset_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Asset_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Asset_Returns_NoContent_On_Success()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId);
        var assetId = await CreateAssetAsync(client, companyId, categoryId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Asset_Returns_NotFound_When_Asset_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var createClient = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(createClient, companyId);
        var assetId = await CreateAssetAsync(createClient, companyId, categoryId);

        using var otherClient = await AdminClient(otherCompanyId);
        var response = await otherClient.DeleteAsync(
            $"/api/companies/{otherCompanyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Asset_Returns_Conflict_When_Asset_Is_Assigned()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateActiveCategoryAsync(client, companyId);
        var assetId = await CreateAssetAsync(client, companyId, categoryId);

        // Assign the asset
        var assignResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments", new
            {
                companyId,
                assetId,
                employeeId = Guid.NewGuid(),
                assignedBy = AdminUserId
            });
        assignResponse.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
