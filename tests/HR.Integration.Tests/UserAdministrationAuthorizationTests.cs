using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Dedicated security-boundary tests for the "users:view"/"users:manage" policies introduced by
/// User Administration. Uses the same dev-persona ids as HR.Modules.Identity's
/// IdentityModule.SeedDevUserAsync (Tom Williams = Employee-only, Laura Bennett = HrAdministrator)
/// seeded directly via TestRoleSeeder, since the dev seeder itself only runs in the real
/// Development-environment host, not the test WebApplicationFactory.
/// </summary>
[Collection("Integration")]
public class UserAdministrationAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid TomWilliamsEmployeeOnly = new("30000000-0000-0000-0000-000000000004");
    private static readonly Guid LauraBennettHrAdmin = new("30000000-0000-0000-0000-000000000005");

    public UserAdministrationAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, TomWilliamsEmployeeOnly, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, LauraBennettHrAdmin, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, LauraBennettHrAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_ListUsers()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_GetUserDetails()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_InviteEmployeeUser()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "someone@test.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_UpdateUserRoles()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);
        var userId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/roles",
            new { companyId, userId, roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_DisableUser()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{Guid.NewGuid()}/disable", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_EnableUser()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/users/{Guid.NewGuid()}/enable", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_ResendInvite()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{Guid.NewGuid()}/resend", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_CancelInvite()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invites/{Guid.NewGuid()}/cancel", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Only_Persona_Is_Forbidden_From_GetUserAuditHistory()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(TomWilliamsEmployeeOnly, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}/audit-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Persona_Can_Access_ListUsers()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(LauraBennettHrAdmin, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
