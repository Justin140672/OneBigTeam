using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Same "platform:admin" policy + allow-list gate pattern as ListCustomersEndpointTests /
/// GetCustomerBillingHistoryEndpointTests — see their remarks. No live Stripe API calls happen in
/// this test host: appsettings.Development.json has no real Stripe:SecretKey configured, so every
/// scenario here exercises the "StripeConfigured = false" branch of GetFailedPaymentsHandler.
/// Real-Stripe-response mapping/join/filtering is covered by GetFailedPaymentsHandlerTests in
/// HR.Modules.Companies.Tests via FakeStripeGateway.
/// </summary>
[Collection("Integration")]
public class GetFailedPaymentsEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetFailedPaymentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, string? email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    [Fact]
    public async Task Get_FailedPayments_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/companies/admin/failed-payments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_FailedPayments_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync("/api/companies/admin/failed-payments");

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_FailedPayments_Returns_Ok_With_No_Payments_When_Stripe_Not_Configured_For_AllowListed_Caller()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync("/api/companies/admin/failed-payments");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<FailedPaymentsPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.StripeConfigured);
        Assert.NotNull(payload.FailedPayments);
        Assert.Empty(payload.FailedPayments);
    }

    [Fact]
    public async Task Get_FailedPayments_Returns_UnprocessableEntity_When_StatusFilter_Is_Invalid()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync("/api/companies/admin/failed-payments?statusFilter=paid");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_FailedPayments_Returns_UnprocessableEntity_When_Search_Exceeds_Max_Length()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);
        var tooLong = new string('a', 201);

        var response = await client.GetAsync($"/api/companies/admin/failed-payments?search={tooLong}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record FailedPaymentsPayload(
        bool StripeConfigured,
        IReadOnlyList<FailedPaymentPayload> FailedPayments);

    private sealed record FailedPaymentPayload(
        Guid CompanyId,
        string CompanyName,
        string SubscriptionStatus,
        string StripeInvoiceId,
        string InvoiceStatus,
        decimal OutstandingAmount,
        string Currency,
        DateTimeOffset InvoiceDate,
        DateTimeOffset? RetryScheduledAt,
        DateTimeOffset? LastSuccessfulPaymentAt,
        decimal? LastSuccessfulPaymentAmount,
        string? HostedInvoiceUrl);
}
