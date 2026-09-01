using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SetPositionRoleDefaultsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SetPositionRoleDefaultsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid? userId = null, Guid? role = null)
    {
        var client = _factory.CreateClient();
        // A fresh actor per call by default: HrAdminUser is a fixed guid and effective roles are
        // resolved suite-wide (UserRoles/UserPositions are not company-scoped), so reusing it let
        // role/position grants from other test files leak in and defeat the role-administration
        // guard this class asserts.
        var effectiveUserId = userId ?? Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, effectiveUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, effectiveUserId, role ?? SystemRoles.HrAdministrator, companyId);
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
    public async Task Put_SetPositionRoleDefaults_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/positions/{positionProfileId}/role-defaults",
            new { companyId, positionProfileId, roleIds = new[] { SystemRoles.Manager } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_SetPositionRoleDefaults_Happy_Path_Sets_Roles_Reflected_By_Subsequent_Get()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var positionProfileId = await CreatePositionProfileAsync(client, companyId, $"Team Lead {Guid.NewGuid():N}");

        var putResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/positions/{positionProfileId}/role-defaults",
            new { companyId, positionProfileId, roleIds = new[] { SystemRoles.Manager, SystemRoles.Employee } });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/positions/role-defaults");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var payload = await getResponse.Content.ReadFromJsonAsync<ListResponsePayload>();
        Assert.NotNull(payload);
        var item = payload!.Positions.Single(p => p.PositionProfileId == positionProfileId);
        Assert.Contains(SystemRoles.Manager, item.RoleIds);
        Assert.Contains(SystemRoles.Employee, item.RoleIds);
    }

    [Fact]
    public async Task Put_SetPositionRoleDefaults_Returns_NotFound_For_Position_Profile_In_Another_Company()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var ownClient = await AuthenticatedClient(ownCompanyId);
        using var otherClient = await AuthenticatedClient(otherCompanyId);

        var otherCompanyPositionId = await CreatePositionProfileAsync(otherClient, otherCompanyId, $"Other Co Role {Guid.NewGuid():N}");

        var response = await ownClient.PutAsJsonAsync(
            $"/api/companies/{ownCompanyId}/positions/{otherCompanyPositionId}/role-defaults",
            new { companyId = ownCompanyId, positionProfileId = otherCompanyPositionId, roleIds = new[] { SystemRoles.Manager } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_SetPositionRoleDefaults_Returns_UnprocessableEntity_For_Unknown_Role_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var positionProfileId = await CreatePositionProfileAsync(client, companyId, $"Analyst {Guid.NewGuid():N}");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/positions/{positionProfileId}/role-defaults",
            new { companyId, positionProfileId, roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_SetPositionRoleDefaults_Returns_Forbidden_When_Actor_Not_Authorised_To_Administer_Requested_Role()
    {
        // HR Administrator may never grant/revoke Company Administrator (mirror-image
        // RoleAdministrationPolicy boundary reused from IAM-02's UpdateUserRoles guard) —
        // including indirectly, via a position's configured default roles.
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var positionProfileId = await CreatePositionProfileAsync(client, companyId, $"Exec {Guid.NewGuid():N}");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/positions/{positionProfileId}/role-defaults",
            new { companyId, positionProfileId, roleIds = new[] { SystemRoles.CompanyAdministrator } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record PositionRoleDefaultItemPayload(Guid PositionProfileId, string Title, bool IsActive, List<Guid> RoleIds);

    private sealed record ListResponsePayload(List<PositionRoleDefaultItemPayload> Positions);
}
