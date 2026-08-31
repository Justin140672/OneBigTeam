using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ExportAccessReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportAccessReviewEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Export_AccessReview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/users/access-review/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_AccessReview_Returns_Forbidden_For_A_Role_Without_UsersManage()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, role: SystemRoles.Manager);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-review/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_AccessReview_Returns_UnprocessableEntity_For_Invalid_Format()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-review/export?format=NotAFormat");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Export_AccessReview_Returns_Csv_With_A_Privileged_Users_Row_On_The_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Export", "Review");
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, employeeId, $"export-review.{Guid.NewGuid():N}@test.com");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"ExportReviewRole.{Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.UserRoles.Add(UserRole.Create(userId, roleId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/users/access-review/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Employee,Email,Role,Source,Override Expires,Expiring Soon", body);
    }
}
