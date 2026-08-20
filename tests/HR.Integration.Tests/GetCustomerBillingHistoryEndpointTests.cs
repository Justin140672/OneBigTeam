using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Same "platform:admin" policy + allow-list gate pattern as
/// GetCustomerBillingBreakdownEndpointTests — see its remarks. No live Stripe API calls happen in
/// this test host: appsettings.Development.json has no real Stripe:SecretKey configured, so every
/// scenario here exercises the "StripeConfigured = false" branch of GetCustomerBillingHistoryHandler
/// (no Stripe API key configured in this environment). Real-Stripe-response mapping is covered by
/// GetCustomerBillingHistoryHandlerTests in HR.Modules.Companies.Tests via FakeStripeGateway.
/// </summary>
[Collection("Integration")]
public class GetCustomerBillingHistoryEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetCustomerBillingHistoryEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Company> SeedCompanyAsync(string name, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var company = Company.Create(Guid.NewGuid(), name, createdAt);
        company.Activate(createdAt);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    [Fact]
    public async Task Get_BillingHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/billing-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_BillingHistory_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/billing-history");

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_BillingHistory_Returns_NotFound_For_Unknown_Company()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{Guid.NewGuid()}/billing-history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_BillingHistory_Returns_Ok_With_No_Invoices_When_Stripe_Not_Configured_For_AllowListed_Caller()
    {
        var now = DateTimeOffset.UtcNow;
        var company = await SeedCompanyAsync($"Billing History Co {Guid.NewGuid()}", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(
            $"/api/companies/admin/customers/{company.Id}/billing-history");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<BillingHistoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(company.Id, payload!.CompanyId);
        Assert.False(payload.StripeConfigured);
        Assert.False(payload.HasStripeCustomer);
        Assert.NotNull(payload.Invoices);
        Assert.Empty(payload.Invoices);
    }

    private sealed record BillingHistoryPayload(
        Guid CompanyId,
        bool StripeConfigured,
        bool HasStripeCustomer,
        IReadOnlyList<BillingHistoryInvoicePayload> Invoices);

    private sealed record BillingHistoryInvoicePayload(
        string StripeInvoiceId,
        DateTimeOffset InvoiceDate,
        decimal Amount,
        string Currency,
        int? EstimatedEmployeeCount,
        string PaymentStatus,
        DateTimeOffset? PaymentDate,
        string? HostedInvoiceUrl);
}
