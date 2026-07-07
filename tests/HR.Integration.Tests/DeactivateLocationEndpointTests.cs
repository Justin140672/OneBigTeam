using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class DeactivateLocationEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("ff000007-0000-0000-0000-000000000001");

    public DeactivateLocationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
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
    public async Task Delete_Location_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Location_Returns_NotFound_When_Location_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Location_Deactivates_Active_Location()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);
        var location = await CreateLocationAsync(client, companyId, locationTypeId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/locations/{location.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await client.GetFromJsonAsync<LocationListPayload>(
            $"/api/companies/{companyId}/locations");
        Assert.NotNull(list);
        Assert.DoesNotContain(list!.Items, i => i.Id == location.Id);
    }

    [Fact]
    public async Task Delete_Location_Returns_NotFound_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);
        var location = await CreateLocationAsync(client, companyId, locationTypeId);

        var first = await client.DeleteAsync(
            $"/api/companies/{companyId}/locations/{location.Id}");
        first.EnsureSuccessStatusCode();

        var second = await client.DeleteAsync(
            $"/api/companies/{companyId}/locations/{location.Id}");

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    private sealed record LocationTypePayload(Guid Id);
    private sealed record LocationPayload(Guid Id);
    private sealed record LocationListPayload(List<LocationItem> Items);
    private sealed record LocationItem(Guid Id);
}
