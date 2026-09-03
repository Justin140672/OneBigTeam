using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CancelSubscription;
using HR.Modules.Companies.Features.CreateBillingPortalSession;
using HR.Modules.Companies.Features.CreateCheckoutSession;
using HR.Modules.Companies.Features.GetCustomerBillingHistory;
using HR.Modules.Companies.Features.ResumeSubscription;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// TEST-003: Stripe API timeout / rejected-request handling for the billing operations.
///
/// Finding: the billing command handlers (CreateCheckoutSession, CreateBillingPortalSession,
/// CancelSubscription, ResumeSubscription) and the read handler GetCustomerBillingHistory do NOT
/// wrap the <see cref="IStripeGateway"/> call in a try/catch — a Stripe failure surfaces as a
/// thrown exception, not a <c>Result.Failure</c>. Only the webhook endpoint maps a
/// <c>StripeException</c> to a 400. These tests pin that behaviour and, critically, assert that
/// no local state is persisted when the gateway call fails (the domain mutation always happens
/// AFTER the gateway call and before a single SaveChanges, so a failure leaves the row untouched).
/// </summary>
public class StripeGatewayFailureTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CancelSubscription_Gateway_Timeout_Propagates_And_Leaves_Subscription_Unchanged()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway { ExceptionToThrowOnCancel = new TimeoutException("Stripe timed out.") };
        var handler = new CancelSubscriptionHandler(
            context, gateway, FakeCurrentTenant.For(companyId.ToString()), new FakeClock(Now));

        await Assert.ThrowsAsync<TimeoutException>(() => handler.HandleAsync(CancellationToken.None));

        var persisted = await BuildContext().CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.False(persisted.CancelAtPeriodEnd);
        Assert.Equal(1, gateway.CancelCallCount);
    }

    [Fact]
    public async Task ResumeSubscription_Gateway_Rejected_Request_Propagates_And_Leaves_Subscription_Unchanged()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        sub.RequestCancellation(new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            ExceptionToThrowOnResume = new HttpRequestException("Stripe rejected the request (503)."),
        };
        var handler = new ResumeSubscriptionHandler(
            context, gateway, FakeCurrentTenant.For(companyId.ToString()), new FakeClock(Now));

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(CancellationToken.None));

        var persisted = await BuildContext().CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(persisted.CancelAtPeriodEnd); // resume never took effect
    }

    [Fact]
    public async Task CreateCheckoutSession_Gateway_Timeout_Propagates()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14));
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway { ExceptionToThrowOnCheckout = new TimeoutException("timeout") };
        var handler = new CreateCheckoutSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            FakeCurrentUser.Authenticated(companyId.ToString()),
            EmptyConfig());

        await Assert.ThrowsAsync<TimeoutException>(() => handler.HandleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateBillingPortalSession_Gateway_Failure_Propagates()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            ExceptionToThrowOnBillingPortal = new HttpRequestException("Stripe unavailable"),
        };
        var handler = new CreateBillingPortalSessionHandler(
            context, gateway, FakeCurrentTenant.For(companyId.ToString()), EmptyConfig());

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCustomerBillingHistory_Gateway_Failure_Propagates_After_AllowList_And_Config_Checks_Pass()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var company = Company.Create(companyId, "Acme", new DateTimeOffset(Now));
        context.Companies.Add(company);
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            ExceptionToThrowOnListInvoices = new TimeoutException("Stripe Invoices list timed out."),
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformAdmin:AllowedEmails:0"] = "admin@example.com",
            })
            .Build();
        var handler = new GetCustomerBillingHistoryHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            config,
            gateway,
            Options.Create(new StripeOptions { SecretKey = "sk_test_123" }));

        await Assert.ThrowsAsync<TimeoutException>(
            () => handler.HandleAsync(new GetCustomerBillingHistoryRequest(companyId), CancellationToken.None));
    }

    [Fact]
    public async Task GetCustomerBillingHistory_Missing_Stripe_Configuration_Fails_Cleanly_Without_Calling_Gateway()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Companies.Add(Company.Create(companyId, "Acme", new DateTimeOffset(Now)));
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        // Gateway would throw if touched — asserts the "no secret key" branch short-circuits first.
        var gateway = new FakeStripeGateway
        {
            ExceptionToThrowOnListInvoices = new InvalidOperationException("gateway must not be called"),
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformAdmin:AllowedEmails:0"] = "admin@example.com",
            })
            .Build();
        var handler = new GetCustomerBillingHistoryHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            config,
            gateway,
            Options.Create(new StripeOptions { SecretKey = "  " }));

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.StripeConfigured);
        Assert.Empty(result.Value.Invoices);
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private readonly string _storeName = "stripe-fail-" + Guid.NewGuid().ToString("N");

    private CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(_storeName)
            .Options;
        return new CompaniesDbContext(options);
    }
}
