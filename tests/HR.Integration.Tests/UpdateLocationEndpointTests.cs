using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateLocationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("ff000006-0000-0000-0000-000000000001");

    public UpdateLocationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateLocationTypeAsync(HttpClient client, Guid companyId, string name = "Office")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LocationTypePayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    private static async Task<LocationPayload> CreateLocationAsync(
        HttpClient client, Guid companyId, Guid locationTypeId, string name = "Head Office")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name,
            locationTypeId
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(payload);
        return payload!;
    }

    [Fact]
    public async Task Put_Location_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/locations/{Guid.NewGuid()}",
            new { name = "Head Office", locationTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Location_Returns_NotFound_When_Location_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/locations/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), name = "Head Office", locationTypeId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Location_Updates_Name_And_Description()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);
        var location = await CreateLocationAsync(client, companyId, locationTypeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/locations/{location.Id}",
            new
            {
                companyId,
                id = location.Id,
                name = "Regional Office",
                description = "Updated description",
                locationTypeId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(payload);
        Assert.Equal(location.Id, payload!.Id);
        Assert.Equal("Regional Office", payload.Name);
        Assert.Equal("Updated description", payload.Description);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Put_Location_Updates_LocationType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var originalTypeId = await CreateLocationTypeAsync(client, companyId, "Office");
        var newTypeId = await CreateLocationTypeAsync(client, companyId, "Warehouse");
        var location = await CreateLocationAsync(client, companyId, originalTypeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/locations/{location.Id}",
            new
            {
                companyId,
                id = location.Id,
                name = location.Name,
                locationTypeId = newTypeId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newTypeId, payload!.LocationTypeId);
    }

    [Fact]
    public async Task Put_Location_Returns_NotFound_When_New_LocationType_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);
        var location = await CreateLocationAsync(client, companyId, locationTypeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/locations/{location.Id}",
            new
            {
                companyId,
                id = location.Id,
                name = location.Name,
                locationTypeId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Location_Returns_Conflict_When_Name_Already_Used()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);
        await CreateLocationAsync(client, companyId, locationTypeId, "Head Office");
        var second = await CreateLocationAsync(client, companyId, locationTypeId, "Branch Office");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/locations/{second.Id}",
            new { companyId, id = second.Id, name = "Head Office", locationTypeId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record LocationTypePayload(Guid Id);

    private sealed record LocationPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        Guid LocationTypeId,
        bool IsActive,
        DateTimeOffset UpdatedAt);
}
