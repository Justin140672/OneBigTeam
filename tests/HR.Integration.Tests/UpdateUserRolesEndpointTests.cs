using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateUserRolesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa2-0000-0000-0000-000000000001");

    public UpdateUserRolesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? AdminUser).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/roles",
            new { companyId, userId, roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{Guid.NewGuid()}/roles",
            new { companyId, userId = Guid.NewGuid(), roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Returns_NotFound_When_User_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var userId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/roles",
            new { companyId, userId, roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Returns_UnprocessableEntity_When_RoleIds_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, "test@test.com");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/roles",
            new { companyId, userId, roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Returns_UnprocessableEntity_When_RoleId_Unknown()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, "test2@test.com");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/roles",
            new { companyId, userId, roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Returns_NotFound_When_User_Belongs_To_Another_Company()
    {
        // IAM-01 regression: caller's own companyId is in the route (passes tenant middleware),
        // but the target userId belongs to a different company's employee — must 404, not update roles.
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(ownCompanyId);
        var otherCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Company");
        var otherCompanyUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, otherCompanyEmployeeId, $"othercompany.{Guid.NewGuid():N}@test.com");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"Role-{Guid.NewGuid():N}");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{ownCompanyId}/users/{otherCompanyUserId}/roles",
            new { companyId = ownCompanyId, userId = otherCompanyUserId, roleIds = new[] { roleId } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdateUserRoles_Replaces_Role_Set_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, "test3@test.com");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"Role-{Guid.NewGuid():N}");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/roles",
            new { companyId, userId, roleIds = new[] { roleId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roles = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Contains(roleId, roles);
    }
}
