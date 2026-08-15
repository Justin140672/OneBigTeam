using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCustomerSupportView;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class GetCustomerSupportViewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Some Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerSupportViewRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerSupportViewRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Fields_For_Active_Subscription_With_Billing_History()
    {
        await using var context = BuildContext();

        var company = Company.Create(Guid.NewGuid(), "Active Co", Now);
        company.Activate(Now);
        context.Companies.Add(company);

        var subscription = CustomerSubscription.StartTrial(company.Id, Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        context.CustomerSubscriptions.Add(subscription);

        for (var i = 0; i < 7; i++)
        {
            context.CustomerBillingSnapshots.Add(CustomerBillingSnapshot.Create(
                company.Id,
                Now.AddHours(-i),
                activeEmployees: 5,
                futureStarters: 0,
                leavers: 0,
                chargeableEmployees: 5,
                pricePerEmployee: 10m,
                discounts: 0m,
                monthlyTotal: 50m));
        }

        await context.SaveChangesAsync();

        var directoryReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 5 };
        var userCountReader = new FakeCompanyUserCountReader { CountToReturn = 8 };
        var jobStatusReader = new FakeBackgroundJobStatusReader
        {
            SummaryToReturn = new(
                Available: true, ServerCount: 2, Enqueued: 1, Processing: 0, Scheduled: 3, Failed: 4, Succeeded: 5, Recurring: 6),
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            directoryReader,
            userCountReader,
            jobStatusReader);

        var result = await handler.HandleAsync(
            new GetCustomerSupportViewRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;

        Assert.Equal(company.Id, value.CompanyId);
        Assert.Equal("Active Co", value.CompanyName);
        Assert.Equal(company.Status.ToString(), value.Status);

        Assert.Equal(SubscriptionStatus.Active.ToString(), value.SubscriptionStatus);
        Assert.Equal(subscription.TrialStartedAt, value.TrialStartedAt);
        Assert.Equal(subscription.TrialExpiresAt, value.TrialExpiresAt);
        Assert.Equal(subscription.CurrentPeriodEnd, value.CurrentPeriodEnd);
        Assert.Equal(subscription.CancelAtPeriodEnd, value.CancelAtPeriodEnd);
        Assert.Equal(subscription.AdminForcedReadOnly, value.AdminForcedReadOnly);

        Assert.Equal(8, value.UserCount);
        Assert.Equal(5, value.ActiveEmployeeCount);
        Assert.Equal(5, value.TotalEmployeeCount);

        Assert.Equal(5, value.RecentBillingSnapshots.Count);
        Assert.True(value.RecentBillingSnapshots
            .Zip(value.RecentBillingSnapshots.Skip(1), (a, b) => a.ComputedAt >= b.ComputedAt)
            .All(inOrder => inOrder));
        Assert.All(value.RecentBillingSnapshots, s =>
        {
            Assert.Equal(5, s.ChargeableEmployees);
            Assert.Equal(50m, s.MonthlyTotal);
        });

        Assert.True(value.BackgroundJobsAvailable);
        Assert.Equal(2, value.BackgroundJobServerCount);
        Assert.Equal(1, value.BackgroundJobsEnqueued);
        Assert.Equal(0, value.BackgroundJobsProcessing);
        Assert.Equal(3, value.BackgroundJobsScheduled);
        Assert.Equal(4, value.BackgroundJobsFailed);
        Assert.Equal(5, value.BackgroundJobsSucceeded);
        Assert.Equal(6, value.BackgroundJobsRecurring);

        Assert.False(value.RecentErrorsAvailable);
        Assert.False(value.RecentEmailsAvailable);
        Assert.False(value.RecentLoginActivityAvailable);
    }

    [Fact]
    public async Task HandleAsync_Defaults_Subscription_Fields_When_Company_Has_No_Subscription_Row()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "No Sub Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerSupportViewRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;

        Assert.Equal("None", value.SubscriptionStatus);
        Assert.Null(value.TrialStartedAt);
        Assert.Null(value.TrialExpiresAt);
        Assert.Null(value.CurrentPeriodEnd);
        Assert.False(value.CancelAtPeriodEnd);
        Assert.False(value.AdminForcedReadOnly);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Billing_History_When_No_Snapshots_Exist()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "No Billing Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerSupportViewRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RecentBillingSnapshots);
    }

    [Fact]
    public async Task HandleAsync_Always_Returns_False_For_Not_Yet_Available_Flags()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Flags Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerSupportViewRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.False(value.RecentErrorsAvailable);
        Assert.False(value.RecentEmailsAvailable);
        Assert.False(value.RecentLoginActivityAvailable);
    }

    private static GetCustomerSupportViewHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakeEmployeeDirectoryReader? employeeDirectoryReader = null,
        FakeCompanyUserCountReader? companyUserCountReader = null,
        FakeBackgroundJobStatusReader? backgroundJobStatusReader = null)
    {
        return new GetCustomerSupportViewHandler(
            context,
            currentUser,
            configuration,
            employeeDirectoryReader ?? new FakeEmployeeDirectoryReader(),
            companyUserCountReader ?? new FakeCompanyUserCountReader(),
            backgroundJobStatusReader ?? new FakeBackgroundJobStatusReader());
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
