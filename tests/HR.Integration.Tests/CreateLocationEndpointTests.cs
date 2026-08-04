using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateLocationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("ff000005-0000-0000-0000-000000000001");

    public CreateLocationEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Post_Locations_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/locations", new
        {
            name = "Head Office",
            locationTypeId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Locations_Creates_Location()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            description = "Main office building",
            locationTypeId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Head Office", payload.Name);
        Assert.Equal("Main office building", payload.Description);
        Assert.Equal(locationTypeId, payload.LocationTypeId);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_Locations_Returns_Conflict_For_Duplicate_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Locations_Returns_NotFound_For_Unknown_LocationType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Locations_Returns_NotFound_When_LocationType_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await AuthenticatedClient(companyA);
        var locationTypeIdForA = await CreateLocationTypeAsync(clientA, companyA);

        using var clientB = await AuthenticatedClient(companyB);

        var response = await clientB.PostAsJsonAsync($"/api/companies/{companyB}/locations", new
        {
            companyId = companyB,
            name = "Branch Office",
            locationTypeId = locationTypeIdForA
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record LocationTypePayload(Guid Id);

    private sealed record LocationPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        Guid LocationTypeId,
        bool IsActive,
        DateTimeOffset CreatedAt);
}
