using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeactivateLocationTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff000003-0000-0000-0000-000000000001");

    public DeactivateLocationTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
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
    public async Task Delete_LocationType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync($"/api/companies/{Guid.NewGuid()}/location-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LocationType_Deactivates_It()
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

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/location-types/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/location-types?isActive=false");
        var list = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(list);
        Assert.Contains(list!.Items, i => i.Id == created.Id && !i.IsActive);
    }

    [Fact]
    public async Task Delete_LocationType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/location-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LocationType_Returns_BadRequest_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = "Depot"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<LocationTypePayload>();
        Assert.NotNull(created);

        var first = await client.DeleteAsync($"/api/companies/{companyId}/location-types/{created!.Id}");
        first.EnsureSuccessStatusCode();

        var second = await client.DeleteAsync($"/api/companies/{companyId}/location-types/{created.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    private sealed record LocationTypePayload(Guid Id, string Name);

    private sealed record ListPayload(IReadOnlyList<ListItem> Items);

    private sealed record ListItem(Guid Id, string Name, bool IsActive);
}
