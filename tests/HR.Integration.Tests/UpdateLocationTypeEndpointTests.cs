using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateLocationTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff000002-0000-0000-0000-000000000001");

    public UpdateLocationTypeEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_LocationType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/location-types/{Guid.NewGuid()}", new
        {
            name = "Office"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_LocationType_Updates_Name_And_Description()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = "Office"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<LocationTypePayload>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/location-types/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                name = "Head Office",
                description = "Main corporate office"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<LocationTypePayload>();
        Assert.NotNull(updated);
        Assert.Equal("Head Office", updated!.Name);
        Assert.Equal("Main corporate office", updated.Description);
    }

    [Fact]
    public async Task Put_LocationType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/location-types/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                name = "Office"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_LocationType_Returns_Conflict_For_Duplicate_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId, name = "Office"
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId, name = "Warehouse"
        });
        create2.EnsureSuccessStatusCode();
        var second = await create2.Content.ReadFromJsonAsync<LocationTypePayload>();
        Assert.NotNull(second);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/location-types/{second!.Id}", new
            {
                companyId,
                id = second.Id,
                name = "Office"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record LocationTypePayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset UpdatedAt);
}
