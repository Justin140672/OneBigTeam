using System.Net;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class EnableUserEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa6-0000-0000-0000-000000000001");

    public EnableUserEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_EnableUser_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{Guid.NewGuid()}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_EnableUser_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{Guid.NewGuid()}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_EnableUser_Returns_NotFound_When_User_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{Guid.NewGuid()}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_EnableUser_Returns_Conflict_When_Already_Active()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, employeeId, $"active.{Guid.NewGuid():N}@test.com", isActive: true);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{userId}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_EnableUser_Returns_NotFound_When_User_Belongs_To_Another_Company()
    {
        // IAM-01 regression: caller's own companyId is in the route (passes tenant middleware),
        // but the target userId belongs to a different company's employee — must 404, not enable.
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(ownCompanyId);
        var otherCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Company");
        var otherCompanyUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, otherCompanyEmployeeId, $"othercompany.{Guid.NewGuid():N}@test.com", isActive: false);

        var response = await client.PostAsync(
            $"/api/companies/{ownCompanyId}/users/{otherCompanyUserId}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await db.Users.FirstAsync(u => u.Id == otherCompanyUserId);
        Assert.False(reloaded.IsActive); // untouched
    }

    [Fact]
    public async Task Post_EnableUser_Enables_User_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, employeeId, $"disabled.{Guid.NewGuid():N}@test.com", isActive: false);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{userId}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await db.Users.FirstAsync(u => u.Id == userId);
        Assert.True(reloaded.IsActive);
    }

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");
}
