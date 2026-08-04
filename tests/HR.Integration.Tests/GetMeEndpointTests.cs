using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves GetMe.CanManageCompany stays in lockstep with the "company:manage" policy
/// (HR.Modules.Identity.IdentityModule.AddRolePolicies). This flag drives the
/// Company Settings UI gate in HR.Web (see CompanyEdit.razor / MainLayout.razor) — if
/// it drifts from the policy, a role either sees an edit UI it can't actually save
/// with, or is hidden a UI it should have.
/// </summary>
[Collection("Integration")]
public class GetMeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Was hardcoded ("dd000001-...") — now that HR.Integration.Tests shares one Postgres
    // Testcontainer/database across the whole assembly instead of one per test class (see
    // IntegrationTestCollection), a fixed literal here could collide with the same literal used
    // by an unrelated test file's own "arbitrary placeholder user" (this one specifically
    // collided with LeaveAuthorizationTests/RecruitmentDashboardSummaryEndpointTests, each
    // assigning the same GUID a different, conflicting role). Guid.NewGuid() is evaluated once
    // per static initialization (i.e. once for this whole test run), so it stays stable across
    // this class's own test methods while being guaranteed unique across every other file.
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
