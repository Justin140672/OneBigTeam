using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetMeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid EmployeeUser = Guid.NewGuid();
    private static readonly Guid ManagerUser = Guid.NewGuid();
    private static readonly Guid HrAdminUser = Guid.NewGuid();
    private static readonly Guid CompanyAdminUser = Guid.NewGuid();

    public GetMeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientFor(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        return client;
    }

    [Fact]
    public async Task Get_Me_Returns_CanManageCompany_False_For_Employee_Role()
    {
        using var client = ClientFor(EmployeeUser);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.CanManageCompany);
    }

    [Fact]
    public async Task Get_Me_Returns_CanManageCompany_False_For_Manager_Role()
    {
        using var client = ClientFor(ManagerUser);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.CanManageCompany);
    }

    [Fact]
    public async Task Get_Me_Returns_CanManageCompany_False_For_HrAdministrator_Role()
    {
        using var client = ClientFor(HrAdminUser);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.CanManageCompany);
    }

    [Fact]
    public async Task Get_Me_Returns_CanManageCompany_True_For_CompanyAdministrator_Role()
    {
        using var client = ClientFor(CompanyAdminUser);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.CanManageCompany);
    }

    [Fact]
    public async Task Get_Me_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Me_Returns_IsEmailConfirmed_True_For_A_Normally_Seeded_User()
    {
        using var client = ClientFor(EmployeeUser);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.IsEmailConfirmed);
    }

    private sealed record MePayload(
        Guid UserId,
        Guid CompanyId,
        string? Email,
        List<Guid> PermissionIds,
        bool CanManageCompany,
        bool IsEmailConfirmed);
}
