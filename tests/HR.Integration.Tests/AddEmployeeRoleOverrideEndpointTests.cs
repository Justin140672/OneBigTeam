using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AddEmployeeRoleOverrideEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa4-0000-0000-0000-000000000001");

    public AddEmployeeRoleOverrideEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid? userId = null, Guid? role = null)
    {
        var client = _factory.CreateClient();
        var effectiveUserId = userId ?? AdminUser;
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, effectiveUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, effectiveUserId, role ?? SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Post_AddEmployeeRoleOverride_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides",
            new { companyId, userId, roleId = SystemRoles.Manager, overrideType = 1, reason = "Reason" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddEmployeeRoleOverride_Returns_NotFound_When_User_Belongs_To_Another_Company()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(ownCompanyId);
        var otherCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Company");
        var otherCompanyUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, otherCompanyEmployeeId, $"othercompany.{Guid.NewGuid():N}@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{ownCompanyId}/users/{otherCompanyUserId}/role-overrides",
            new { companyId = ownCompanyId, userId = otherCompanyUserId, roleId = SystemRoles.Manager, overrideType = 1, reason = "Reason" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddEmployeeRoleOverride_Returns_UnprocessableEntity_When_RoleId_Unknown()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, $"unknown-role.{Guid.NewGuid():N}@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides",
            new { companyId, userId, roleId = Guid.NewGuid(), overrideType = 1, reason = "Reason" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddEmployeeRoleOverride_Returns_Forbidden_For_Self_Elevation()
    {
        var companyId = Guid.NewGuid();
        var actorEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Self", "Elevator");
        var actorId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, actorEmployeeId, $"self-elevate.{Guid.NewGuid():N}@test.com");
        using var client = await AuthenticatedClient(companyId, actorId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/users/{actorId}/role-overrides",
            new { companyId, userId = actorId, roleId = SystemRoles.Manager, overrideType = 1, reason = "Trying to elevate self" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddEmployeeRoleOverride_Returns_Forbidden_When_Role_Outside_Actors_Administrable_Set()
    {
        // HR Administrator (the seeded actor) may never grant/deny Company Administrator.
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, $"outside-boundary.{Guid.NewGuid():N}@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides",
            new { companyId, userId, roleId = SystemRoles.CompanyAdministrator, overrideType = 1, reason = "Attempted out-of-scope grant" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddEmployeeRoleOverride_Creates_Override_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, $"happy-path.{Guid.NewGuid():N}@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides",
            new { companyId, userId, roleId = SystemRoles.Manager, overrideType = 1, reason = "Covering for a colleague on leave" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/users/{userId}/role-overrides");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var payload = await getResponse.Content.ReadFromJsonAsync<ListResponsePayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Overrides, o => o.RoleId == SystemRoles.Manager);
    }

    private sealed record OverrideItemPayload(Guid RoleId, string OverrideType, string Reason, DateTimeOffset? ExpiresAt);

    private sealed record ListResponsePayload(List<OverrideItemPayload> Overrides);
}
