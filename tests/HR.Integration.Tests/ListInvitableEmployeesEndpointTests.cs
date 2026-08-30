using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListInvitableEmployeesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaab1-0000-0000-0000-000000000001");

    public ListInvitableEmployeesEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/users/invitable-employees";

    [Fact]
    public async Task Get_InvitableEmployees_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_InvitableEmployees_Returns_Forbidden_For_Persona_Without_UsersManage()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_InvitableEmployees_Returns_Current_Employee_Without_An_Account()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Invitable", "Person");

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == employeeId);
    }

    [Fact]
    public async Task Get_InvitableEmployees_Excludes_Employee_With_An_Account()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var invitableId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Still", "Invitable");
        var withAccountId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Has", "Account");
        await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, withAccountId, "hasaccount@test.com");

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == invitableId);
        Assert.DoesNotContain(payload.Items, i => i.EmployeeId == withAccountId);
    }

    [Fact]
    public async Task Get_InvitableEmployees_Excludes_Employee_With_Pending_Invite()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var pendingId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Pending", "Invitee");
        await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, pendingId, "pending@test.com");

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == pendingId);
    }

    [Fact]
    public async Task Get_InvitableEmployees_Includes_Employee_Whose_Only_Invite_Is_Expired()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var expiredId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Expired", "Invitee");
        await IdentityUserAdminTestHelpers.SeedInviteAsync(
            _factory, companyId, expiredId, "expired@test.com",
            createdAt: DateTimeOffset.UtcNow.AddDays(-30));

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == expiredId);
    }

    [Fact]
    public async Task Get_InvitableEmployees_Returns_Forbidden_For_Another_Tenants_Company()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Tenant");

        // client authenticated against ownCompanyId, but requesting otherCompanyId's roster
        using var client = AuthenticatedClient(ownCompanyId);

        var response = await client.GetAsync(Url(otherCompanyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record Payload(IReadOnlyList<ItemPayload> Items);

    private sealed record ItemPayload(
        Guid EmployeeId,
        string Name,
        string? WorkEmail,
        Guid? PositionProfileId,
        string? PositionTitle);
}
