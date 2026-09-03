using System.Net;
using System.Net.Http.Headers;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stripe;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class StripeWebhookEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public StripeWebhookEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.StripeGateway.Reset();
    }

    private async Task<Guid> SeedCompanyWithSubscriptionAsync(string stripeCustomerId, string stripeSubscriptionId)
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        subscription.ActivateSubscription(stripeCustomerId, stripeSubscriptionId, "price_1", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return companyId;
    }

    private static HttpRequestMessage BuildRequest(string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/companies/stripe-webhook")
        {
            Content = new StringContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (signature is not null)
        {
            request.Headers.Add("Stripe-Signature", signature);
        }

        return request;
    }

    [Fact]
    public async Task Post_StripeWebhook_Is_Reachable_Anonymously()
    {
        using var client = _factory.CreateClient();
        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "unknown.event.type", null, null, null, null, null, null, null);

        var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=fake"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_StripeWebhook_Returns_BadRequest_When_Signature_Header_Missing()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(BuildRequest("{}", signature: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_StripeWebhook_Returns_BadRequest_When_Gateway_Rejects_Invalid_Signature()
    {
        using var client = _factory.CreateClient();
        _factory.StripeGateway.ExceptionToThrowOnConstructEvent = new StripeException("Invalid signature.");

        var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=invalid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_StripeWebhook_Returns_Ok_For_Unknown_Event_Type()
    {
        using var client = _factory.CreateClient();
        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "invoice.payment_failed", null, null, null, null, null, null, null);

        var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=fake"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_StripeWebhook_CheckoutSessionCompleted_Activates_Matched_Subscription()
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        db.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14));
        await db.SaveChangesAsync();

        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "checkout.session.completed",
            "cus_webhook_test",
            "sub_webhook_test",
            companyId,
            CurrentPeriodEnd: null,
            CancelAtPeriodEnd: null,
            StripeStatus: null,
            PriceId: "price_webhook_test");

        using var client = _factory.CreateClient();
        var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=fake"));
        response.EnsureSuccessStatusCode();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await verifyDb.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal("cus_webhook_test", persisted.StripeCustomerId);
    }

    [Fact]
    public async Task Post_StripeWebhook_Invalid_Signature_Is_Rejected_Before_Any_State_Change()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_sig_test", "sub_sig_test");
        _factory.StripeGateway.ExceptionToThrowOnConstructEvent = new StripeException("Invalid signature.");
        // Also arm an event: if processing were (wrongly) reached it would activate/cancel the row.
        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "customer.subscription.deleted", "cus_sig_test", "sub_sig_test", null, null, null, "canceled", null);

        using var client = _factory.CreateClient();
        var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=invalid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await verifyDb.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.False(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task Post_StripeWebhook_Duplicate_Delivery_Applies_Business_Change_At_Most_Once()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_dup_test", "sub_dup_test");

        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "customer.subscription.deleted", "cus_dup_test", "sub_dup_test", null,
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddDays(10), CancelAtPeriodEnd: null,
            StripeStatus: "canceled", PriceId: null);

        using var client = _factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=fake"));
            response.EnsureSuccessStatusCode();
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var rows = await verifyDb.CustomerSubscriptions.Where(s => s.CompanyId == companyId).ToListAsync();
        var persisted = Assert.Single(rows);
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task Post_StripeWebhook_SubscriptionUpdated_Updates_Matched_Subscription()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_update_test", "sub_update_test");

        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "customer.subscription.updated",
            "cus_update_test",
            "sub_update_test",
            CompanyId: null,
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddMonths(2),
            CancelAtPeriodEnd: true,
            StripeStatus: "past_due",
            PriceId: null);

        using var client = _factory.CreateClient();
        var response = await client.SendAsync(BuildRequest("{}", "t=1,v1=fake"));
        response.EnsureSuccessStatusCode();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var persisted = await verifyDb.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
    }
}
