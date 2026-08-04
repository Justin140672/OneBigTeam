using HR.Modules.Companies.Features.CreateCheckoutSession;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class CreateCheckoutSessionHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_CheckoutUrl_From_Gateway_On_Happy_Path()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway { CheckoutUrlToReturn = "https://checkout.stripe.com/abc123" };
        var handler = new CreateCheckoutSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            FakeCurrentUser.Authenticated(companyId.ToString()),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.stripe.com/abc123", result.Value!.CheckoutUrl);
        Assert.Equal(companyId, gateway.LastCompanyId);
        Assert.Equal("test@example.com", gateway.LastCustomerEmail);
    }

    [Fact]
    public async Task HandleAsync_Passes_Existing_StripeCustomerId_To_Gateway_When_Present()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_existing", "sub_existing", "price_1", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway();
        var handler = new CreateCheckoutSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            FakeCurrentUser.Authenticated(companyId.ToString()),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("cus_existing", gateway.LastExistingStripeCustomerId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_Failure_When_No_Tenant_Context()
    {
        await using var context = BuildContext();
        var gateway = new FakeStripeGateway();
        var handler = new CreateCheckoutSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.None,
            FakeCurrentUser.Authenticated(),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_Failure_When_No_User_Email()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var gateway = new FakeStripeGateway();
        var handler = new CreateCheckoutSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            new FakeCurrentUser(Guid.NewGuid(), email: null, tenantId: companyId.ToString()),
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
        var handler = new CreateCheckoutSessionHandler(
            context,
            gateway,
            FakeCurrentTenant.For(companyId.ToString()),
            FakeCurrentUser.Authenticated(companyId.ToString()),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
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
