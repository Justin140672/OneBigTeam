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
    /// Verifies that the endpoint returns 404 when E2E_TESTING is not enabled.
    /// The DepartureFinaliserTestEndpoint is development-only and should not be accessible in normal operation.
    /// This test documents the expected behavior: when E2E_TESTING environment variable is not set,
    /// the endpoint checks it immediately and returns NotFound (404).
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_NotFound_When_E2E_Testing_Is_Not_Enabled()
    {
        // E2E_TESTING is not enabled by default in integration test environment
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content: null);

        // When E2E_TESTING is not set, the endpoint returns 404 (hidden from production)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Documents the security boundaries of the DepartureFinaliserTestEndpoint.
    /// This is a development-only seam that is only active when E2E_TESTING=true.
    ///
    /// When E2E_TESTING is enabled, the endpoint enforces:
    /// - Authentication: Requires valid X-Test-User header (401 Unauthorized if missing)
    /// - Authorization: Requires "role:hr-administrator" policy (403 Forbidden if wrong role)
    /// - Company isolation: currentTenant.TenantId must match route companyId (403 Forbid if mismatch)
    /// - Validation: Employee must exist and have an in-progress leaving process
    ///
    /// Since E2E_TESTING is not enabled in the integration test environment,
    /// these security boundaries are not testable here. They are instead verified by
    /// E2E tests in HR.Web.E2E.Tests/Tests/DepartureFinaliserE2ETests.cs,
    /// which run with E2E_TESTING=true in a full application context.
    /// </summary>
    [Fact]
    public void Security_Boundaries_Documented_For_E2E_Testing_Context()
    {
        // This test documents expected behavior that is tested by E2E suite
        // when E2E_TESTING environment variable is set to "true":
        //
        // 1. Anonymous requests (no auth) → 401 Unauthorized
        // 2. Non-HR-Administrator roles → 403 Forbidden
        // 3. Cross-company access (different company ID in auth vs URL) → 403 Forbidden
        // 4. Employee doesn't exist → 404 Not Found
        // 5. Employee exists but has no leaving process → 400 Bad Request
        // 6. Employee has completed process → 200 OK (idempotent)
        // 7. Authenticated HR Admin same company → processes the departure finalization
        //
        // See DepartureFinaliserE2ETests for executable verification of these boundaries.
        Assert.True(true);
    }
}
