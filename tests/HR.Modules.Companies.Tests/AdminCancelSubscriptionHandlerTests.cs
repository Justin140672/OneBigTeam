using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.AdminCancelSubscription;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class AdminCancelSubscriptionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var gateway = new FakeStripeGateway();
        var handler = BuildHandler(
            context,
            gateway,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new AdminCancelSubscriptionRequest { CompanyId = companyId, Reason = "Support cancellation" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(publisher.Published);
        Assert.Null(gateway.LastCancelledStripeSubscriptionId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Subscription_Row_Exists()
    {
        await using var context = BuildContext();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeStripeGateway(),
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new AdminCancelSubscriptionRequest { CompanyId = Guid.NewGuid(), Reason = "Support cancellation" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Subscription_Is_Already_Canceled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(1));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var gateway = new FakeStripeGateway();
        var handler = BuildHandler(
            context,
            gateway,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new AdminCancelSubscriptionRequest { CompanyId = companyId, Reason = "Support cancellation" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);
        Assert.Null(gateway.LastCancelledStripeSubscriptionId);
    }

    [Fact]
    public async Task HandleAsync_Cancels_Calls_Gateway_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var gateway = new FakeStripeGateway();
        var handler = BuildHandler(
            context,
            gateway,
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new AdminCancelSubscriptionRequest { CompanyId = companyId, Reason = "Customer requested via support" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CancelAtPeriodEnd);
        Assert.Equal("sub_1", gateway.LastCancelledStripeSubscriptionId);
        Assert.True(gateway.LastCancelAtPeriodEnd);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(persisted.CancelAtPeriodEnd);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<SubscriptionCancelledByAdminAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal("Customer requested via support", auditEvent.Reason);
    }

    private static AdminCancelSubscriptionHandler BuildHandler(
        CompaniesDbContext context,
        HR.Modules.Companies.Services.IStripeGateway stripeGateway,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher)
    {
        return new AdminCancelSubscriptionHandler(
            context,
            stripeGateway,
            currentUser,
            configuration,
            new FakeClock(Now.UtcDateTime),
            auditEventPublisher);
    }

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var builder = new ConfigurationBuilder();

        if (allowedEmails.Length > 0)
        {
            var data = allowedEmails
                .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
                .ToArray();
            builder.AddInMemoryCollection(data);
        }
        else
        {
            builder.AddInMemoryCollection();
        }

        return builder.Build();
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
