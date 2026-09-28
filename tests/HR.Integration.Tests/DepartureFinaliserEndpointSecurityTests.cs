using System.Net;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Security and access control tests for the DepartureFinaliserTestEndpoint.
/// This endpoint is a development-only seam that manually triggers employee departure finalization.
/// It is only accessible when E2E_TESTING=true and the caller is an HR Administrator.
///
/// The endpoint's security boundaries are enforced at the handler level:
/// - E2E_TESTING gate: returns 404 when the environment variable is not "true" (production mode)
/// - Authentication: requires valid X-Test-User header (401 Unauthorized if missing)
/// - Authorization: requires "role:hr-administrator" policy (403 Forbidden if wrong role)
/// - Company isolation: currentTenant.TenantId must match route companyId (403 Forbidden if mismatch)
/// - Validation: employee must exist and have appropriate leaving process status
/// - Process selection: correctly selects InProgress process even when cancelled/completed processes exist
/// - Idempotency: returns 200 for already-completed processes (Former Employee + Completed status)
///
/// This test class verifies both the production-disabled behavior (E2E_TESTING not set) and
/// the authorization boundaries when E2E_TESTING=true.
/// </summary>
[Collection("Integration")]
public class DepartureFinaliserEndpointSecurityTests
{
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinaliserEndpointSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    /// <summary>
    /// Verifies that the endpoint returns 404 when E2E_TESTING environment variable is not set.
    /// The endpoint is completely hidden in production/normal-dev mode.
    /// This gate prevents accidental exposure of development testing infrastructure in production.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_404_When_E2E_Testing_Disabled()
    {
        // When E2E_TESTING is not set (default), the endpoint must return 404 (hidden from production)
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies that the endpoint returns 401 (Unauthorized) for anonymous requests when E2E_TESTING=true.
    /// The endpoint requires authentication via the test auth mechanism.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_401_When_E2E_Enabled_And_Anonymous()
    {
        // Set E2E_TESTING so the endpoint is reachable
        Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        try
        {
            var employeeId = Guid.NewGuid();
            var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

            using var client = _factory.CreateClient();
            // Don't set X-Test-User header — should get 401
            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", null);
        }
    }

    /// <summary>
    /// Verifies that the endpoint returns 403 (Forbidden) for non-HR-Administrator roles when E2E_TESTING=true.
    /// Only users with the hr-administrator policy can access the endpoint.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_403_When_E2E_Enabled_And_Wrong_Role()
    {
        // Set E2E_TESTING so the endpoint is reachable
        Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        try
        {
            var managerUserId = Guid.Parse("30000000-0000-0000-0000-000000000002");  // James Okafor, Manager
            var employeeId = Guid.NewGuid();
            var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

            // Set up test auth for a Manager role (not HR Administrator)
            _factory.SupabaseAuthGateway.SetTestUser(managerUserId, AcmeCompanyId, "Manager");

            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", managerUserId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", null);
        }
    }

    /// <summary>
    /// Verifies that the endpoint returns 403 (Forbidden) for cross-company access when E2E_TESTING=true.
    /// A user from one company cannot access employees from another company.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_403_When_E2E_Enabled_And_Cross_Company()
    {
        // Set E2E_TESTING so the endpoint is reachable
        Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        try
        {
            var hrAdminUserId = Guid.Parse("30000000-0000-0000-0000-000000000008");  // David Park, HR Admin (Acme)
            var betaCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000002");  // Beta Corp
            var employeeId = Guid.NewGuid();
            var url = $"/api/dev/departure-finaliser/{betaCompanyId:N}/{employeeId:N}";

            // Set up test auth for an HR Admin from Acme, trying to access Beta employee
            _factory.SupabaseAuthGateway.SetTestUser(hrAdminUserId, AcmeCompanyId, "HR Administrator");

            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", hrAdminUserId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", null);
        }
    }
}
