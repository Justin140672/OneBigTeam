using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class SubscriptionStatusReaderTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetStatusAsync_Returns_TrialDaysRemaining_And_Not_ReadOnly_While_Trial_In_Progress()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var reader = new SubscriptionStatusReader(context, new FakeClock(Now.AddDays(4)));

        var snapshot = await reader.GetStatusAsync(companyId, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.Trial, snapshot.Status);
        Assert.False(snapshot.IsReadOnly);
        Assert.Equal(10, snapshot.TrialDaysRemaining);
    }

    [Fact]
    public async Task GetStatusAsync_Marks_Expired_And_Persists_When_Trial_Has_Elapsed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var reader = new SubscriptionStatusReader(context, new FakeClock(Now.AddDays(15)));

        var snapshot = await reader.GetStatusAsync(companyId, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.TrialExpired, snapshot.Status);
        Assert.True(snapshot.IsReadOnly);
        Assert.Equal(0, snapshot.TrialDaysRemaining);

        // Confirm the transition was actually persisted, not just computed in-memory.
        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.TrialExpired, persisted.Status);
    }

    [Fact]
    public async Task GetStatusAsync_Returns_Not_ReadOnly_For_Active_Subscription()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddYears(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var reader = new SubscriptionStatusReader(context, new FakeClock(Now.AddDays(30)));

        var snapshot = await reader.GetStatusAsync(companyId, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.Active, snapshot.Status);
        Assert.False(snapshot.IsReadOnly);
        Assert.Equal(0, snapshot.TrialDaysRemaining);
    }

    [Fact]
    public async Task GetStatusAsync_Returns_TrialExpired_ReadOnly_When_No_Subscription_Row_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var reader = new SubscriptionStatusReader(context, new FakeClock(Now));

        var snapshot = await reader.GetStatusAsync(companyId, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.TrialExpired, snapshot.Status);
        Assert.True(snapshot.IsReadOnly);
        Assert.Equal(0, snapshot.TrialDaysRemaining);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
