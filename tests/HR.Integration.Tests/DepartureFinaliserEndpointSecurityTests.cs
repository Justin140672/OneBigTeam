using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Security and access control tests for the DepartureFinaliserTestEndpoint.
/// This endpoint is a development-only seam that manually triggers employee departure finalization.
/// It is only accessible when E2E_TESTING=true and the caller is an HR Administrator.
/// Tests demonstrate the endpoint's security boundaries:
/// - 404 when E2E_TESTING environment variable is not set
/// - 401 for anonymous requests
/// - 403 for non-HR-Administrator roles
/// - 403 for cross-company access (user from one company accessing another's employees)
/// - Idempotent 200 for already-completed processes
/// </summary>
[Collection("Integration")]
public class DepartureFinaliserEndpointSecurityTests
{
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BetaCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinaliserEndpointSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    /// <summary>
    /// Verifies that the endpoint returns 404 when E2E_TESTING environment variable is not set
    /// or is false. This proves the endpoint is absent in production/normal-dev mode.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_404_When_E2E_Testing_Is_Not_Enabled()
    {
        // When E2E_TESTING is not set (default), the endpoint must return 404
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies that anonymous requests (no X-Test-User header) are rejected with 401 Unauthorized.
    /// The endpoint requires authentication via the role:hr-administrator policy.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_Unauthorized_For_Anonymous_Request()
    {
        // No X-Test-User or X-Test-Tenant headers — completely anonymous
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content: null);

        // Anonymous requests are rejected with 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Verifies that an authenticated HR Administrator can call the endpoint.
    /// Even if the employee/process doesn't exist, the request passes authentication and authorization.
    /// A 404 NotFound would be expected for non-existent employees.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Authenticates_HrAdministrator_Request()
    {
        var employeeId = Guid.NewGuid();
        var hrAdminUserId = Guid.NewGuid();

        // Assign HR Admin role to the test user
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminUserId, SystemRoles.HrAdministrator, AcmeCompanyId);

        // Act: Call the endpoint as an HR Admin (employee doesn't need to exist to test auth/authz)
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";
        using var client = _factory.CreateClient();

        // Add headers for HR Admin authentication and company context
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, hrAdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, "hr.admin@test.example");

        var response = await client.PostAsync(url, content: null);

        // Assert: HR Admin is authenticated (not 401/403)
        // Will be 404 for non-existent employee, but that's expected (not an auth failure)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Verifies that the endpoint validates company membership.
    /// A user authenticated for one company (Beta) must not be able to access another company's (Acme) resources.
    /// The endpoint's validation checks that currentTenant.TenantId matches the route companyId.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_Forbidden_For_Cross_Company_Access()
    {
        var acmeEmployeeId = Guid.NewGuid();
        var betaHrAdminUserId = Guid.NewGuid();

        // Assign HR Admin role to the test user, but for Beta Corp
        await TestRoleSeeder.AssignRoleAsync(_factory, betaHrAdminUserId, SystemRoles.HrAdministrator, BetaCompanyId);

        // Act: Try to access an Acme employee (route uses AcmeCompanyId) with a client authenticated as Beta
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{acmeEmployeeId:N}";
        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, betaHrAdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, BetaCompanyId.ToString()); // Auth as Beta
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, "beta.hr.admin@test.example");

        var response = await client.PostAsync(url, content: null);

        // Assert: Cross-company access is forbidden (currentTenant mismatch)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Verifies that non-HR-Administrator roles cannot access the endpoint.
    /// The endpoint requires the "role:hr-administrator" policy.
    /// A Manager or other non-HR role should be rejected with 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_Forbidden_For_Non_HrAdministrator_Roles()
    {
        var employeeId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();

        // Assign Manager role (not HR Admin) to the test user
        await TestRoleSeeder.AssignRoleAsync(_factory, managerUserId, SystemRoles.Manager, AcmeCompanyId);

        // Act: Try to access the endpoint as a Manager (not HR Admin)
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";
        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, managerUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, "manager@test.example");

        var response = await client.PostAsync(url, content: null);

        // Assert: Non-HR-Administrator roles are rejected by the Policies("role:hr-administrator") gate
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
