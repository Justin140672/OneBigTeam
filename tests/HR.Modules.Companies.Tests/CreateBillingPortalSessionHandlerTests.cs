using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CreateBillingPortalSession;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class CreateBillingPortalSessionHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_PortalUrl_From_Gateway_On_Happy_Path()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", now.AddMonths(1), now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway { PortalUrlToReturn = "https://billing.stripe.com/portal-abc" };
        var handler = new CreateBillingPortalSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://billing.stripe.com/portal-abc", result.Value!.PortalUrl);
        Assert.Equal("cus_1", gateway.LastBillingPortalStripeCustomerId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_Failure_When_No_Tenant_Context()
    {
        await using var context = BuildContext();
        var gateway = new FakeStripeGateway();
        var handler = new CreateBillingPortalSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.None,
            BuildConfiguration());

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
        var handler = new CreateBillingPortalSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_No_Stripe_Customer_Id()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway();
        var handler = new CreateBillingPortalSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Null(gateway.LastBillingPortalStripeCustomerId);
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
