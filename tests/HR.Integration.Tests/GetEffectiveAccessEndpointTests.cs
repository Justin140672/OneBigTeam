using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HR.Modules.Identity.Persistence;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEffectiveAccessEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa9-0000-0000-0000-000000000001");

    public GetEffectiveAccessEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<(Guid positionId, Guid roleId)> SeedPositionWithRoleAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var now = DateTimeOffset.UtcNow;
        var positionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        db.Positions.Add(Position.Create(positionId, companyId, "Effective Access Position", now));
        db.Roles.Add(Role.Create(roleId, $"EffectiveAccessRole.{Guid.NewGuid():N}", now));
        db.PositionRoles.Add(PositionRole.Create(positionId, roleId, now));
        db.UserPositions.Add(UserPosition.Create(employeeId, positionId, now));

        await db.SaveChangesAsync();

        return (positionId, roleId);
    }

    private async Task SeedRoleOverrideAsync(Guid companyId, Guid employeeId, Guid roleId, EmployeeRoleOverrideType overrideType, string reason)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.EmployeeRoleOverrides.Add(
            EmployeeRoleOverride.Create(companyId, employeeId, roleId, overrideType, reason, null, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_EffectiveAccess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}/effective-access");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EffectiveAccess_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee, companyId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}/effective-access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EffectiveAccess_Returns_NotFound_When_Employee_Belongs_To_Another_Company()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(ownCompanyId);

        var otherCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Company");
        var otherCompanyUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, otherCompanyEmployeeId, $"cross-tenant.{Guid.NewGuid():N}@test.com");

        var response = await client.GetAsync($"/api/companies/{ownCompanyId}/users/{otherCompanyUserId}/effective-access");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_EffectiveAccess_Returns_OK_With_Roles_Position_And_Overrides_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Effective", "Access");
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, employeeId, $"effective-access.{Guid.NewGuid():N}@test.com");

        var (positionId, inheritedRoleId) = await SeedPositionWithRoleAsync(companyId, userId);
        await SeedRoleOverrideAsync(companyId, userId, inheritedRoleId, EmployeeRoleOverrideType.Deny, "Covering audit");

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{userId}/effective-access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<EffectiveAccessPayload>();
        Assert.NotNull(payload);
        Assert.Equal(userId, payload!.EmployeeId);
        Assert.NotNull(payload.Position);
        Assert.Equal(positionId, payload.Position!.Id);
        Assert.Contains(payload.Overrides, o => o.RoleId == inheritedRoleId && o.OverrideType == "Deny");
        Assert.DoesNotContain(payload.EffectiveRoles, r => r.RoleId == inheritedRoleId);
    }

    [Fact]
    public async Task Get_EffectiveAccess_Returns_UnprocessableEntity_Or_NotFound_For_Malformed_Route_Ids()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/not-a-guid/effective-access");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Unexpected status code for malformed route id: {response.StatusCode}");
    }

    private sealed record PositionSummaryPayload(Guid Id, string Name);
    private sealed record RoleOverridePayload(Guid Id, Guid RoleId, string RoleName, string OverrideType, string Reason, DateTimeOffset? ExpiresAt, bool IsActive);
    private sealed record EffectiveRolePayload(Guid RoleId, string RoleName, List<string> Sources);

    private sealed record EffectiveAccessPayload(
        Guid EmployeeId,
        Guid? UserId,
        string EmployeeName,
        PositionSummaryPayload? Position,
        List<RoleOverridePayload> Overrides,
        List<EffectiveRolePayload> EffectiveRoles);
}
