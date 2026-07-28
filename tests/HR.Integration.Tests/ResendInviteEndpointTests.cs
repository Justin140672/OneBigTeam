using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class ResendInviteEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa3-0000-0000-0000-000000000001");

    public ResendInviteEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_ResendInvite_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{Guid.NewGuid()}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResendInvite_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{Guid.NewGuid()}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResendInvite_Returns_NotFound_When_Invite_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{Guid.NewGuid()}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResendInvite_Returns_Conflict_When_Already_Claimed()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
            _factory, companyId, employeeId, "claimed@test.com", claimed: true);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{inviteId}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResendInvite_Returns_Conflict_When_Already_Cancelled()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
            _factory, companyId, employeeId, "cancelled@test.com", cancelled: true);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{inviteId}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResendInvite_Regenerates_Token_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var inviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
            _factory, companyId, employeeId, "pending@test.com");

        string originalToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            originalToken = (await db.UserInvites.FirstAsync(i => i.Id == inviteId)).Token;
        }

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{inviteId}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await db2.UserInvites.FirstAsync(i => i.Id == inviteId);
        Assert.NotEqual(originalToken, reloaded.Token);
    }

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");
}
