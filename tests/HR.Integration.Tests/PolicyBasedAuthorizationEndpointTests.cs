using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// IAM-06: end-to-end proof that a representative sample of real endpoints gated by
/// <see cref="HR.Modules.Identity.Authorization.PolicyCatalog"/> policies actually enforce
/// permission-based access through the live ASP.NET Core authorization pipeline (as opposed to the
/// unit-level <c>PolicyMatrixTests</c>/<c>PermissionAuthorizationHandlerTests</c>, which exercise the
/// data and the handler in isolation). The `GetEffectiveAccessEndpointTests` file already covers the
/// "users:manage" policy end-to-end (the endpoint most directly connected to this ticket), so this
/// file covers two additional policies instead of duplicating that coverage.
/// </summary>
[Collection("Integration")]
public class PolicyBasedAuthorizationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public PolicyBasedAuthorizationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    // --- "asset:view" (granted to Employee, Manager, HrAdministrator — not Recruiter) ---
    // GET /api/companies/{companyId}/employees/{employeeId}/assets

    [Fact]
    public async Task Get_EmployeeAssets_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeAssets_Returns_Forbidden_For_Role_Without_AssetView_Permission()
    {
        var companyId = Guid.NewGuid();
        // Recruiter does not hold "asset:view" per RolePermissionConfiguration.cs.
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Recruiter);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeAssets_Returns_OK_For_Role_With_AssetView_Permission()
    {
        var companyId = Guid.NewGuid();
        // Employee holds "asset:view" per RolePermissionConfiguration.cs.
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- "leave:approve" (granted to Manager, HrAdministrator — not Employee) ---
    // GET /api/companies/{companyId}/leave-policies

    [Fact]
    public async Task Get_LeavePolicies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavePolicies_Returns_Forbidden_For_Role_Without_LeaveApprove_Permission()
    {
        var companyId = Guid.NewGuid();
        // Employee does not hold "leave:approve" per RolePermissionConfiguration.cs.
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavePolicies_Returns_OK_For_Role_With_LeaveApprove_Permission()
    {
        var companyId = Guid.NewGuid();
        // Manager holds "leave:approve" per RolePermissionConfiguration.cs.
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
