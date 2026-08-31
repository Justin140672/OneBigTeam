using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SearchUserAccessEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SearchUserAccessEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_SearchUserAccess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/users/access-search");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SearchUserAccess_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, role: SystemRoles.Employee);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-search");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_SearchUserAccess_Returns_UnprocessableEntity_For_Invalid_PageSize()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-search?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_SearchUserAccess_Returns_UnprocessableEntity_For_Invalid_OverrideState()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-search?overrideState=NotAValue");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_SearchUserAccess_Returns_OK_With_A_Row_For_A_User_With_A_Direct_Role_On_The_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Access", "Search");
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, employeeId, $"access-search.{Guid.NewGuid():N}@test.com");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"AccessSearchRole.{Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.UserRoles.Add(UserRole.Create(userId, roleId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-search?roleId={roleId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == userId && i.DirectRoles.Any(r => r.RoleId == roleId));
    }

    private sealed record RoleRefPayload(Guid RoleId, string RoleName);
    private sealed record SearchItemPayload(Guid EmployeeId, Guid? UserId, string Name, string Email, List<RoleRefPayload> DirectRoles);
    private sealed record SearchPayload(List<SearchItemPayload> Items, int TotalCount, int Page, int PageSize);
}
