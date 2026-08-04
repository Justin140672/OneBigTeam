using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CancelSubscription;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CancelSubscriptionHandlerTests
{
    [Fact]
    public async Task HandleAsync_Calls_Gateway_And_Requests_Cancellation_On_Happy_Path()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", now.AddMonths(1), now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway();
        var clock = new FakeClock(now.UtcDateTime);
        var handler = new CancelSubscriptionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            clock);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CancelAtPeriodEnd);
        Assert.Equal("sub_1", gateway.LastCancelledStripeSubscriptionId);
        Assert.True(gateway.LastCancelAtPeriodEnd);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_Failure_When_No_Tenant_Context()
    {
        await using var context = BuildContext();
        var gateway = new FakeStripeGateway();
        var handler = new CancelSubscriptionHandler(
            context,
            gateway,
            FakeCurrentTenant.None,
            new FakeClock(DateTime.UtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_Failure_When_No_Subscription_Row_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var gateway = new FakeStripeGateway();
        var handler = new CancelSubscriptionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            new FakeClock(DateTime.UtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_No_Stripe_Subscription_Id()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway();
        var handler = new CancelSubscriptionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            new FakeClock(DateTime.UtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Null(gateway.LastCancelledStripeSubscriptionId);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
