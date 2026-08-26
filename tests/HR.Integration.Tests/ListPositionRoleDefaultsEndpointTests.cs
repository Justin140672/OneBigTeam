using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListPositionRoleDefaultsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("eeeeeeee-0000-0000-0000-000000000005");

    public ListPositionRoleDefaultsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid LeavePolicyId)> SeedReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var departmentResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"Engineering {Guid.NewGuid():N}"
        });
        departmentResponse.EnsureSuccessStatusCode();
        var department = await departmentResponse.Content.ReadFromJsonAsync<IdPayload>();

        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = $"Office Type {Guid.NewGuid():N}"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationType = await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>();

        var locationResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = $"Head Office {Guid.NewGuid():N}",
            locationTypeId = locationType!.Id
        });
        locationResponse.EnsureSuccessStatusCode();
        var location = await locationResponse.Content.ReadFromJsonAsync<IdPayload>();

        var leavePolicyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"Standard Leave {Guid.NewGuid():N}",
            carryOverDays = 5,
            allowNegativeBalance = false
        });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var leavePolicy = await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>();

        return (department!.Id, location!.Id, leavePolicy!.Id);
    }

    private static async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Get_PositionRoleDefaults_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/positions/role-defaults");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PositionRoleDefaults_Returns_Active_Position_Profiles_For_The_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var positionProfileId = await CreatePositionProfileAsync(client, companyId, $"Software Developer {Guid.NewGuid():N}");

        var response = await client.GetAsync($"/api/companies/{companyId}/positions/role-defaults");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListResponsePayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Positions, p => p.PositionProfileId == positionProfileId && p.RoleIds.Count == 0);
    }

    [Fact]
    public async Task Get_PositionRoleDefaults_Does_Not_Leak_Position_Profiles_From_Another_Company()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var ownClient = await AuthenticatedClient(ownCompanyId);
        using var otherClient = await AuthenticatedClient(otherCompanyId);

        var otherCompanyPositionId = await CreatePositionProfileAsync(otherClient, otherCompanyId, $"Other Co Role {Guid.NewGuid():N}");

        var response = await ownClient.GetAsync($"/api/companies/{ownCompanyId}/positions/role-defaults");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListResponsePayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Positions, p => p.PositionProfileId == otherCompanyPositionId);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record PositionRoleDefaultItemPayload(Guid PositionProfileId, string Title, bool IsActive, List<Guid> RoleIds);

    private sealed record ListResponsePayload(List<PositionRoleDefaultItemPayload> Positions);
}
