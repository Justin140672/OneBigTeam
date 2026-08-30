using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class InviteEmployeeUserEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa1-0000-0000-0000-000000000001");

    public InviteEmployeeUserEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_InviteEmployeeUser_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "test@example.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "test@example.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_UnprocessableEntity_For_Invalid_Email()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "not-an-email", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_UnprocessableEntity_When_RoleIds_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "test@example.com", roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_NotFound_When_Employee_Unknown()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/invite-user",
            new { companyId, employeeId = Guid.NewGuid(), email = "test@example.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_Conflict_When_Employee_Already_Has_Linked_User()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, "already@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "test@example.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Creates_Invite_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"Role-{Guid.NewGuid():N}");
        var email = $"invite.{Guid.NewGuid():N}@test.com";

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email, roleIds = new[] { roleId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<InvitePayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.Equal(email, payload.Email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var invite = await db.UserInvites.FirstOrDefaultAsync(i => i.EmployeeId == employeeId);
        Assert.NotNull(invite);
        Assert.Contains(roleId, invite!.PendingRoleIds);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_Conflict_When_Pending_Invite_Already_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, "pending@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "again@test.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record InvitePayload(Guid InviteId, Guid EmployeeId, string Email, DateTimeOffset ExpiresAt);
}
