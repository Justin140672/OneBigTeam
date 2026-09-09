using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): AssetCategory.version coverage for
// PUT .../asset-categories/{id}, against the real Postgres-backed ApiWebApplicationFactory. Follows
// UpdateAssetCategoryEndpointTests for auth helpers. The pre-edit Version is read from the
// ListAssetCategories item, which carries it.
[Collection("Integration")]
public class UpdateAssetCategoryConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bbcc0002-0000-0000-0000-000000000098");

    public UpdateAssetCategoryConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_AssetCategory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/asset-categories/{Guid.NewGuid()}", new { name = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await CreateCategoryAsync();
        var version = (await GetItemAsync(client, companyId, id)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{id}", Body(companyId, id, description: "EditorA", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<CategoryPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{id}", Body(companyId, id, description: "EditorB", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        Assert.Equal("EditorA", (await GetItemAsync(client, companyId, id)).Description);
    }

    [Fact]
    public async Task Put_AssetCategory_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await CreateCategoryAsync();
        var version = (await GetItemAsync(client, companyId, id)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{id}", Body(companyId, id, description: "Updated", expectedVersion: version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated", payload.Description);
    }

    [Fact]
    public async Task Put_AssetCategory_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id) = await CreateCategoryAsync();

        var before = await GetItemAsync(client, companyId, id);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories/{id}", Body(companyId, id, description: "NoVersion1", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetItemAsync(client, companyId, id);
        Assert.Equal(before.Description, after.Description);
    }

    private static object Body(Guid companyId, Guid id, string description, int? expectedVersion)
        => new { companyId, id, name = "Electronics", description, expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> CreateCategoryAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
        return (client, companyId, created.Id);
    }

    private static async Task<CategoryPayload> GetItemAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/asset-categories");
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<List<CategoryPayload>>())!;
        return items.Single(i => i.Id == id);
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record CategoryPayload(Guid Id, string Name, string? Description, bool IsActive, int Version);
}
