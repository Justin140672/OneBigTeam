using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCustomerDashboard;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class GetCustomerDashboardHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_AllowList_Is_Empty()
    {
        await using var context = BuildContext();
        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Is_Missing_Even_With_AllowList()
    {
        await using var context = BuildContext();
        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: null),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Email_On_AllowList_Case_Insensitively()
    {
        await using var context = BuildContext();
        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "ADMIN@EXAMPLE.COM"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Counts_All_Companies_Including_Those_With_No_Subscription_Row()
    {
        await using var context = BuildContext();
        var activeCompany = Company.Create(Guid.NewGuid(), "Active Co", Now);
        activeCompany.Activate(Now);
        var pendingCompany = Company.Create(Guid.NewGuid(), "Pending Co", Now);
        context.Companies.AddRange(activeCompany, pendingCompany);
        await context.SaveChangesAsync();

        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCustomers);
        Assert.Equal(1, result.Value.ActiveCustomers);
    }

    [Fact]
    public async Task HandleAsync_Maps_Subscription_Statuses_To_Correct_Buckets()
    {
        await using var context = BuildContext();

        var trialCompany = Company.Create(Guid.NewGuid(), "Trial Co", Now);
        var trialSubscription = CustomerSubscription.StartTrial(trialCompany.Id, Now, trialLengthDays: 14);

        var expiredCompany = Company.Create(Guid.NewGuid(), "Expired Co", Now);
        var expiredSubscription = CustomerSubscription.StartTrial(expiredCompany.Id, Now, trialLengthDays: 14);
        expiredSubscription.MarkExpiredIfNeeded(Now.AddDays(20));

        var canceledCompany = Company.Create(Guid.NewGuid(), "Canceled Co", Now);
        var canceledSubscription = CustomerSubscription.StartTrial(canceledCompany.Id, Now, trialLengthDays: 14);
        canceledSubscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        canceledSubscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(1));

        var pastDueCompany = Company.Create(Guid.NewGuid(), "PastDue Co", Now);
        var pastDueSubscription = CustomerSubscription.StartTrial(pastDueCompany.Id, Now, trialLengthDays: 14);
        pastDueSubscription.ActivateSubscription("cus_2", "sub_2", "price_1", Now.AddMonths(1), Now);
        pastDueSubscription.UpdateFromStripe(SubscriptionStatus.PastDue, Now.AddMonths(1), cancelAtPeriodEnd: false, Now.AddDays(1));

        var activeCompany = Company.Create(Guid.NewGuid(), "ActiveSub Co", Now);
        var activeSubscription = CustomerSubscription.StartTrial(activeCompany.Id, Now, trialLengthDays: 14);
        activeSubscription.ActivateSubscription("cus_3", "sub_3", "price_1", Now.AddMonths(1), Now);

        context.Companies.AddRange(trialCompany, expiredCompany, canceledCompany, pastDueCompany, activeCompany);
        context.CustomerSubscriptions.AddRange(
            trialSubscription, expiredSubscription, canceledSubscription, pastDueSubscription, activeSubscription);
        await context.SaveChangesAsync();

        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TrialCustomers);
        Assert.Equal(1, result.Value.ReadOnlyCustomers);
        Assert.Equal(1, result.Value.CancelledSubscriptions);
    }

    [Fact]
    public async Task HandleAsync_RecentRegistrations_Returns_At_Most_10_Ordered_By_CreatedAt_Desc()
    {
        await using var context = BuildContext();
        var companies = Enumerable.Range(0, 12)
            .Select(i => Company.Create(Guid.NewGuid(), $"Company {i}", Now.AddMinutes(i)))
            .ToList();
        context.Companies.AddRange(companies);
        await context.SaveChangesAsync();

        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.RecentRegistrations.Count);
        Assert.Equal("Company 11", result.Value.RecentRegistrations[0].CompanyName);
        Assert.Equal("Company 2", result.Value.RecentRegistrations[9].CompanyName);
        Assert.True(
            result.Value.RecentRegistrations
                .Zip(result.Value.RecentRegistrations.Skip(1))
                .All(pair => pair.First.RegisteredAt >= pair.Second.RegisteredAt));
    }

    [Fact]
    public async Task HandleAsync_RecentSubscriptionChanges_Returns_At_Most_10_Ordered_By_UpdatedAt_Desc_With_Company_Name_And_Status()
    {
        await using var context = BuildContext();
        var companies = new List<Company>();
        var subscriptions = new List<CustomerSubscription>();

        for (var i = 0; i < 12; i++)
        {
            var company = Company.Create(Guid.NewGuid(), $"Sub Company {i}", Now);
            var subscription = CustomerSubscription.StartTrial(company.Id, Now, trialLengthDays: 14);
            subscription.ActivateSubscription("cus", "sub", "price", Now.AddMonths(1), Now.AddMinutes(i));
            companies.Add(company);
            subscriptions.Add(subscription);
        }

        context.Companies.AddRange(companies);
        context.CustomerSubscriptions.AddRange(subscriptions);
        await context.SaveChangesAsync();

        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.RecentSubscriptionChanges.Count);
        Assert.Equal("Sub Company 11", result.Value.RecentSubscriptionChanges[0].CompanyName);
        Assert.Equal(SubscriptionStatus.Active.ToString(), result.Value.RecentSubscriptionChanges[0].Status);
        Assert.True(
            result.Value.RecentSubscriptionChanges
                .Zip(result.Value.RecentSubscriptionChanges.Skip(1))
                .All(pair => pair.First.ChangedAt >= pair.Second.ChangedAt));
    }

    [Fact]
    public async Task HandleAsync_PendingPermanentDeletions_Is_Zero_When_None_Scheduled()
    {
        await using var context = BuildContext();
        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PendingPermanentDeletions);
    }

    [Fact]
    public async Task HandleAsync_PendingPermanentDeletions_Counts_Only_Scheduled_And_Not_Cancelled_Or_Executed()
    {
        await using var context = BuildContext();

        var pendingCompany = Company.Create(Guid.NewGuid(), "Pending Deletion Co", Now);
        var pendingSubscription = CustomerSubscription.StartTrial(pendingCompany.Id, Now, trialLengthDays: 14);
        pendingSubscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);

        var cancelledCompany = Company.Create(Guid.NewGuid(), "Cancelled Deletion Co", Now);
        var cancelledSubscription = CustomerSubscription.StartTrial(cancelledCompany.Id, Now, trialLengthDays: 14);
        cancelledSubscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        cancelledSubscription.CancelScheduledDeletion(Now.AddDays(1));

        var executedCompany = Company.Create(Guid.NewGuid(), "Executed Deletion Co", Now);
        var executedSubscription = CustomerSubscription.StartTrial(executedCompany.Id, Now, trialLengthDays: 14);
        executedSubscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        executedSubscription.ExecuteDeletion(Now.AddDays(1));

        context.Companies.AddRange(pendingCompany, cancelledCompany, executedCompany);
        context.CustomerSubscriptions.AddRange(pendingSubscription, cancelledSubscription, executedSubscription);
        await context.SaveChangesAsync();

        var handler = new GetCustomerDashboardHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PendingPermanentDeletions);
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
