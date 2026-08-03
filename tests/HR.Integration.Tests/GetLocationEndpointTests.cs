using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetLocationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff000008-0000-0000-0000-000000000001");

    public GetLocationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
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
    public async Task Get_Location_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Location_Returns_Location_For_Authenticated_Request()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyId}/locations/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Head Office", payload.Name);
        Assert.Equal(locationTypeId, payload.LocationTypeId);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Get_Location_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Location_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var client = AdminClient(companyA);

        var locationTypeId = await CreateLocationTypeAsync(client, companyA);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyA}/locations", new
        {
            companyId = companyA,
            name = "Head Office",
            locationTypeId
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(created);

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.GetAsync($"/api/companies/{companyB}/locations/{created!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record LocationTypePayload(Guid Id);

    private sealed record LocationPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        Guid LocationTypeId,
        bool IsActive);
}
