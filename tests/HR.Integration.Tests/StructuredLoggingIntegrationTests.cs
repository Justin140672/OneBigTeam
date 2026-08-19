using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Infrastructure.Logging;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies that the structured logging middleware (correlation ID, request logging)
/// is active and behaves correctly for all requests.
/// </summary>
[Collection("Integration")]
public class StructuredLoggingIntegrationTests(ApiWebApplicationFactory factory)
{
    // ─── Correlation ID ────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_Without_Correlation_Id_Header_Gets_One_Generated()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/startup-migrations");

        Assert.True(
            response.Headers.Contains(CorrelationIdMiddleware.HeaderName),
            $"Response should contain {CorrelationIdMiddleware.HeaderName} header.");

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(correlationId), "Generated correlation ID should not be empty.");
        Assert.True(Guid.TryParse(correlationId, out _), $"Auto-generated correlation ID '{correlationId}' should be a valid GUID.");
    }

    [Fact]
    public async Task Request_With_Correlation_Id_Header_Echoes_It_Back_Unchanged()
    {
        using var client = factory.CreateClient();
        var expected = Guid.NewGuid().ToString("D");
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, expected);

        var response = await client.GetAsync("/health/startup-migrations");

        var returned = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(expected, returned);
    }

    [Fact]
    public async Task Correlation_Id_Is_Present_Even_For_Unauthenticated_Requests()
    {
        // Middleware runs before authentication, so even 401 responses carry a correlation ID.
        using var client = factory.CreateClient();

        // POST /api/companies (CreateCompany) was removed in 78a43344. This test only cares that
        // some authenticated POST route exercises the logging/correlation-id middleware, so it's
        // deliberately provider-agnostic about which endpoint it hits —
        // /api/company-onboarding/checklist/dismiss (no {companyId} route segment) fits.
        var response = await client.PostAsJsonAsync("/api/company-onboarding/checklist/dismiss", new { name = "Test" });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task Each_Request_Gets_A_Distinct_Correlation_Id_When_None_Is_Provided()
    {
        using var client = factory.CreateClient();

        var r1 = await client.GetAsync("/health/startup-migrations");
        var r2 = await client.GetAsync("/health/startup-migrations");

        var id1 = r1.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        var id2 = r2.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task Correlation_Id_Provided_By_Client_Survives_Multiple_Hops()
    {
        using var client = factory.CreateClient();
        var correlationId = "e2e-" + Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        // Two separate requests with the same ID — each should echo it back.
        var r1 = await client.GetAsync("/health/startup-migrations");
        var r2 = await client.GetAsync("/health/startup-migrations");

        Assert.Equal(correlationId, r1.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        Assert.Equal(correlationId, r2.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    // ─── Request logging middleware smoke test ─────────────────────────────────

    [Fact]
    public async Task Request_Logging_Middleware_Does_Not_Break_Normal_Request_Flow()
    {
        // If middleware has a bug, requests would throw or return 500.
        // A successful response (even 401 for anon) confirms the pipeline is healthy.
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/startup-migrations");

        // 200 OK or 503 (migration failure) are both valid — 500 would indicate middleware fault.
        Assert.NotEqual(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Request_Logging_Middleware_Does_Not_Interfere_With_Auth_Flow()
    {
        // Verifies that the logging middleware does not swallow or corrupt the response.
        // An unauthenticated request should receive either 401 (not authenticated) or
        // 405 (no matching route for this method) but never 500 (middleware fault).
        using var client = factory.CreateClient();

        // POST /api/companies (CreateCompany) was removed in 78a43344. This test only cares that
        // some authenticated POST route exercises the logging/correlation-id middleware, so it's
        // deliberately provider-agnostic about which endpoint it hits —
        // /api/company-onboarding/checklist/dismiss (no {companyId} route segment) fits.
        var response = await client.PostAsJsonAsync("/api/company-onboarding/checklist/dismiss", new { name = "Test" });

        Assert.NotEqual(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }
}
