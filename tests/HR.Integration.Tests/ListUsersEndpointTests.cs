using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ListUsersEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa7-0000-0000-0000-000000000001");

    public ListUsersEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_ListUsers_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListUsers_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListUsers_Returns_Empty_When_No_Invites_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
    }

    [Fact]
    public async Task Get_ListUsers_Returns_Row_For_Invited_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Invited", "Person");
        await IdentityUserAdminTestHelpers.SeedInviteAsync(_factory, companyId, employeeId, "invited@test.com");

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        var item = payload.Items[0];
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Pending", item.InvitationStatus);
        Assert.Equal("NoAccount", item.AccountStatus);
    }

    [Fact]
    public async Task Get_ListUsers_Does_Not_Include_Employees_Never_Invited()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "NeverInvited", "Person");

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_ListUsers_Returns_UnprocessableEntity_For_Invalid_PageSize()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ListPayload(IReadOnlyList<ListItemPayload> Items, int TotalCount, int Page, int PageSize);

    private sealed record ListItemPayload(
        Guid EmployeeId,
        Guid? UserId,
        string Name,
        string Email,
        IReadOnlyList<Guid> RoleIds,
        IReadOnlyList<string> RoleNames,
        string AccountStatus,
        string InvitationStatus,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset CreatedAt);
}
