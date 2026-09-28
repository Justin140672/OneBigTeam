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
/// This test class verifies the production-disabled behavior (E2E_TESTING not set).
/// The full authorization matrix and process selection logic are verified by E2E tests
/// that run with E2E_TESTING=true in the HR.Web.E2E.Tests suite.
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
}
