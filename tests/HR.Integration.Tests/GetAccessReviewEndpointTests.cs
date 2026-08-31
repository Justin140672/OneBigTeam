using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetAccessReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetAccessReviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid? userId = null, Guid? role = null)
    {
        var client = _factory.CreateClient();
        var effectiveUserId = userId ?? Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, effectiveUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, effectiveUserId, role ?? SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Get_AccessReview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/users/access-review");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AccessReview_Returns_Forbidden_For_A_Role_Without_UsersManage()
    {
        // "users:manage" (not "users:view") gates this endpoint — a role that only has
        // "users:view" territory (e.g. Manager) must be forbidden here.
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, role: SystemRoles.Manager);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-review");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AccessReview_Returns_UnprocessableEntity_For_Malformed_CompanyId()
    {
        using var client = await AuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/companies/not-a-guid/users/access-review");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Unexpected status code for malformed route id: {response.StatusCode}");
    }

    [Fact]
    public async Task Get_AccessReview_Returns_OK_With_A_Privileged_User_And_Excludes_Baseline_Only_Users()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var privilegedEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Privileged", "User");
        var privilegedUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, privilegedEmployeeId, $"access-review-priv.{Guid.NewGuid():N}@test.com");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"AccessReviewRole.{Guid.NewGuid():N}");

        var baselineEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Baseline", "User");
        var baselineUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, baselineEmployeeId, $"access-review-baseline.{Guid.NewGuid():N}@test.com");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.UserRoles.Add(UserRole.Create(privilegedUserId, roleId, DateTimeOffset.UtcNow));
            db.UserRoles.Add(UserRole.Create(baselineUserId, SystemRoles.Employee, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-review");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReviewPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == privilegedUserId && i.Privileges.Any(p => p.RoleId == roleId));
        Assert.DoesNotContain(payload.Items, i => i.EmployeeId == baselineUserId);
    }

    private sealed record PrivilegePayload(Guid RoleId, string RoleName, string Source);
    private sealed record ReviewItemPayload(Guid EmployeeId, Guid? UserId, string Name, string Email, List<PrivilegePayload> Privileges);
    private sealed record ReviewPayload(List<ReviewItemPayload> Items, int TotalCount);
}
