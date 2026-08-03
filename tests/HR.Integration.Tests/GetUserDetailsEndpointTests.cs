using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetUserDetailsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa8-0000-0000-0000-000000000001");

    public GetUserDetailsEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_UserDetails_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_UserDetails_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_UserDetails_Returns_NotFound_When_No_Invite_And_No_User()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UserDetails_Returns_Details_For_Invited_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Details", "Person");
        await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, "details@test.com");

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{employeeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DetailsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.Equal("Pending", payload.InvitationStatus);
        Assert.Equal("NoAccount", payload.AccountStatus);
    }

    private sealed record DetailsPayload(
        Guid EmployeeId,
        Guid? UserId,
        string Name,
        string Email,
        IReadOnlyList<Guid> RoleIds,
        IReadOnlyList<string> RoleNames,
        string AccountStatus,
        string InvitationStatus,
        Guid? InviteId,
        DateTimeOffset? InviteExpiresAt,
        string? CreatedByName,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset CreatedAt);
}
