using HR.Infrastructure.Logging;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 23 (P2): <see cref="CorrelationIdMiddleware"/> behaviour against the real ASP.NET Core
/// pipeline. Uses the anonymous <c>/health/startup-migrations</c> endpoint (the same anonymous
/// target <c>StructuredLoggingIntegrationTests</c> uses) purely as a lightweight request target
/// that needs no authentication setup — this suite is only interested in the
/// <see cref="CorrelationIdMiddleware.HeaderName"/> request/response header handling, not in
/// anything the endpoint itself does.
/// </summary>
[Collection("Integration")]
public class CorrelationIdMiddlewareEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CorrelationIdMiddlewareEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Valid_Guid_Header_Is_Echoed_Back_Unchanged()
    {
        using var client = _factory.CreateClient();
        const string supplied = "11111111-1111-1111-1111-111111111111";
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.Equal(supplied, echoed, ignoreCase: true);
    }

    [Fact]
    public async Task Valid_NFormat_Guid_Header_Is_Accepted_And_Echoed_As_A_Guid()
    {
        // "N" format (no dashes) is one of the formats Guid.TryParse accepts without qualification.
        var supplied = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, supplied.ToString("N"));

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.True(Guid.TryParse(echoed, out var echoedGuid));
        Assert.Equal(supplied, echoedGuid);
    }

    [Fact]
    public async Task Missing_Header_Generates_A_Valid_Guid()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.True(Guid.TryParse(echoed, out var generated));
        Assert.NotEqual(Guid.Empty, generated);
    }

    [Fact]
    public async Task Two_Anonymous_Requests_With_No_Header_Get_Different_Generated_Ids()
    {
        using var client = _factory.CreateClient();

        var first = await client.GetAsync("/health/startup-migrations");
        var second = await client.GetAsync("/health/startup-migrations");

        var firstId = GetCorrelationHeader(first);
        var secondId = GetCorrelationHeader(second);

        Assert.NotEqual(firstId, secondId, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_Guid_Opaque_String_Header_Within_Policy_Is_Echoed_Back_Verbatim()
    {
        // Ticket 23 follow-up: the accepted correlation-id policy is length + character allow-list,
        // NOT "must be a GUID" — a real caller (or test harness) supplying an opaque, non-GUID id
        // (e.g. "e2e-<guid>") within that policy must have it honoured and echoed back unchanged,
        // not silently replaced. See CorrelationIdMiddleware remarks / StructuredLoggingIntegrationTests
        // .Correlation_Id_Provided_By_Client_Survives_Multiple_Hops for the same expectation.
        using var client = _factory.CreateClient();
        const string supplied = "not-a-guid";
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.Equal(supplied, echoed);
    }

    [Fact]
    public async Task Prefixed_Opaque_Correlation_Id_Within_Policy_Is_Echoed_Back_Verbatim()
    {
        using var client = _factory.CreateClient();
        var supplied = "e2e-" + Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.Equal(supplied, echoed);
    }

    [Fact]
    public async Task Oversized_Header_Is_Replaced_With_A_Freshly_Generated_Guid()
    {
        using var client = _factory.CreateClient();
        // > 128 chars, and otherwise made of allowed characters, so length alone must be why it's
        // rejected — proving the length guard fires independently of the character allow-list.
        var supplied = new string('a', 200);
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.NotEqual(supplied, echoed, StringComparer.OrdinalIgnoreCase);
        Assert.True(Guid.TryParse(echoed, out _));
    }

    [Fact]
    public async Task Header_With_Disallowed_Characters_Is_Replaced_With_A_Freshly_Generated_Guid()
    {
        using var client = _factory.CreateClient();
        // Spaces and angle brackets are outside the allow-list even though the value is short.
        var supplied = "<script>alert(1)</script>";
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.NotEqual(supplied, echoed, StringComparer.OrdinalIgnoreCase);
        Assert.True(Guid.TryParse(echoed, out _));
    }

    [Fact]
    public async Task Header_Value_At_Exactly_The_Maximum_Allowed_Length_Boundary_Is_Accepted()
    {
        // Exactly MaxHeaderLength (128) allowed characters — the length policy accepts values AT
        // the boundary, only rejecting values that exceed it (see Oversized_Header_Is_Replaced...).
        using var client = _factory.CreateClient();
        var supplied = new string('a', 128);
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/health/startup-migrations");

        var echoed = GetCorrelationHeader(response);
        Assert.Equal(supplied, echoed);
    }

    private static string GetCorrelationHeader(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        return Assert.Single(values);
    }
}
