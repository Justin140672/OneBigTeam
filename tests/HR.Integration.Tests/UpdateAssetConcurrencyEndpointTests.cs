using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): Asset.version coverage for PUT .../assets/{id},
// against the real Postgres-backed ApiWebApplicationFactory. Follows UpdateAssetEndpointTests for
// seeding/auth helpers. The pre-edit Version is read from GET .../assets/{id}, which carries it.
[Collection("Integration")]
public class UpdateAssetConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bbcc0002-0000-0000-0000-000000000097");

    public UpdateAssetConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Asset_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/assets/{Guid.NewGuid()}", new { assetNumber = "A", name = "L" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, categoryId, assetId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, assetId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}", Body(companyId, assetId, categoryId, name: "EditorA", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<AssetPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}", Body(companyId, assetId, categoryId, name: "EditorB", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        Assert.Equal("EditorA", (await GetAsync(client, companyId, assetId)).Name);
    }

    [Fact]
    public async Task Put_Asset_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, categoryId, assetId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, assetId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}", Body(companyId, assetId, categoryId, name: "Updated", expectedVersion: version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AssetPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated", payload.Name);
    }

    [Fact]
    public async Task Put_Asset_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, categoryId, assetId) = await SeedAsync();

        var before = await GetAsync(client, companyId, assetId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}", Body(companyId, assetId, categoryId, name: "NoVersion1", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, assetId);
        Assert.Equal(before.Name, after.Name);
    }

    private static object Body(Guid companyId, Guid assetId, Guid categoryId, string name, int? expectedVersion)
        => new { companyId, id = assetId, assetNumber = "ASSET-001", categoryId, name, expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid CategoryId, Guid AssetId)> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var categoryResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });
        categoryResponse.EnsureSuccessStatusCode();
        var categoryId = (await categoryResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var assetResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets",
            new { companyId, assetNumber = "ASSET-001", categoryId, name = "Laptop" });
        assetResponse.EnsureSuccessStatusCode();
        var assetId = (await assetResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (client, companyId, categoryId, assetId);
    }

    private static async Task<AssetPayload> GetAsync(HttpClient client, Guid companyId, Guid assetId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{assetId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetPayload>())!;
    }

    private sealed record IdPayload(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record AssetPayload(Guid Id, string AssetNumber, string Name, int Version);
}
