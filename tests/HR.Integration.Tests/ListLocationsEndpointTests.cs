using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ListLocationsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff000009-0000-0000-0000-000000000001");

    public ListLocationsEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_Locations_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/locations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Locations_Returns_Empty_List_When_None_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_Locations_Returns_Created_Locations()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Branch Office",
            locationTypeId
        });
        create2.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Equal("Branch Office", payload.Items[0].Name);
        Assert.Equal("Head Office", payload.Items[1].Name);
        Assert.All(payload.Items, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Get_Locations_Excludes_Inactive_By_Default()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var create = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(created);

        var deactivate = await client.DeleteAsync($"/api/companies/{companyId}/locations/{created!.Id}");
        deactivate.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_Locations_Includes_Inactive_When_Requested()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var locationTypeId = await CreateLocationTypeAsync(client, companyId);

        var create = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = "Head Office",
            locationTypeId
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<LocationPayload>();
        Assert.NotNull(created);

        var deactivate = await client.DeleteAsync($"/api/companies/{companyId}/locations/{created!.Id}");
        deactivate.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/locations?includeInactive=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.Id == created.Id && !i.IsActive);
    }

    [Fact]
    public async Task Get_Locations_Scopes_To_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AdminClient(companyA);
        var locationTypeId = await CreateLocationTypeAsync(clientA, companyA);
        var create = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/locations", new
        {
            companyId = companyA,
            name = "Head Office",
            locationTypeId
        });
        create.EnsureSuccessStatusCode();

        using var clientB = AdminClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record LocationTypePayload(Guid Id);

    private sealed record LocationPayload(Guid Id);

    private sealed record ListPayload(IReadOnlyList<LocationItem> Items);

    private sealed record LocationItem(
        Guid Id,
        string Name,
        Guid LocationTypeId,
        bool IsActive);
}
