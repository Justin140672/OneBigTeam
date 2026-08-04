using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetSubscriptionDetails;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

public class GetSubscriptionDetailsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Details_With_Standard_Plan_Name_When_Price_Matches_Configured_Price()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_standard", now.AddMonths(1), now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var employeeDirectoryReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 7 };
        var handler = new GetSubscriptionDetailsHandler(
            context,
            employeeDirectoryReader,
            FakeCurrentTenant.For(companyId.ToString()),
            Options.Create(new StripeOptions { PriceId = "price_standard" }));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, result.Value!.Status);
        Assert.Equal("Standard Plan", result.Value.PlanName);
        Assert.Equal(7, result.Value.ActiveEmployeeCount);
        Assert.Equal(subscription.CurrentPeriodEnd, result.Value.NextBillingDate);
        Assert.False(result.Value.CancelAtPeriodEnd);
        Assert.Equal(companyId, employeeDirectoryReader.LastCompanyId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Raw_PriceId_As_PlanName_When_It_Does_Not_Match_Configured_Price()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var subscription = CustomerSubscription.StartTrial(companyId, now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_other", now.AddMonths(1), now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var handler = new GetSubscriptionDetailsHandler(
            context,
            new FakeEmployeeDirectoryReader(),
            FakeCurrentTenant.For(companyId.ToString()),
            Options.Create(new StripeOptions { PriceId = "price_standard" }));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("price_other", result.Value!.PlanName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_PlanName_When_No_PriceId_Set()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var handler = new GetSubscriptionDetailsHandler(
            context,
            new FakeEmployeeDirectoryReader(),
            FakeCurrentTenant.For(companyId.ToString()),
            Options.Create(new StripeOptions { PriceId = "price_standard" }));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PlanName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_Failure_When_No_Tenant_Context()
    {
        await using var context = BuildContext();
        var handler = new GetSubscriptionDetailsHandler(
            context,
            new FakeEmployeeDirectoryReader(),
            FakeCurrentTenant.None,
            Options.Create(new StripeOptions()));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_Failure_When_No_Subscription_Row_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new GetSubscriptionDetailsHandler(
            context,
            new FakeEmployeeDirectoryReader(),
            FakeCurrentTenant.For(companyId.ToString()),
            Options.Create(new StripeOptions()));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
