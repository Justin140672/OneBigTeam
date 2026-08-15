using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCustomerDetails;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

public class GetCustomerDetailsHandlerTests
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

        var result = await handler.HandleAsync(new GetCustomerDetailsRequest(company.Id), CancellationToken.None);

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
            new GetCustomerDetailsRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Fields_For_Active_Subscription_With_Settings()
    {
        await using var context = BuildContext();

        var company = Company.Create(Guid.NewGuid(), "Active Co", Now);
        company.Activate(Now);
        var settings = CompanySettings.CreateDefault(company.Id, Now);
        company.SetSettings(settings, Now);
        context.Companies.Add(company);

        var subscription = CustomerSubscription.StartTrial(company.Id, Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        context.CustomerSubscriptions.Add(subscription);

        await context.SaveChangesAsync();

        var employeeReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 5 };
        var storageReader = new FakeDocumentStorageReader
        {
            UsageToReturn = new DocumentStorageUsage(TotalStorageBytes: 12345, FileCount: 3),
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            employeeReader,
            storageReader,
            monthlyPriceGbp: 49m);

        var result = await handler.HandleAsync(
            new GetCustomerDetailsRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(company.Id, value.CompanyId);
        Assert.Equal("Active Co", value.CompanyName);
        Assert.Equal(company.Status.ToString(), value.Status);
        Assert.Equal(company.CreatedAt, value.CreatedAt);
        Assert.Equal(company.UpdatedAt, value.UpdatedAt);
        Assert.Equal(SubscriptionStatus.Active.ToString(), value.SubscriptionStatus);
        Assert.Equal(subscription.TrialStartedAt, value.TrialStartedAt);
        Assert.Equal(subscription.TrialExpiresAt, value.TrialExpiresAt);
        Assert.Equal(subscription.CurrentPeriodEnd, value.CurrentPeriodEnd);
        Assert.Equal(subscription.CancelAtPeriodEnd, value.CancelAtPeriodEnd);
        Assert.Equal(49m, value.MonthlyCharge);
        Assert.Equal(5, value.ActiveEmployeeCount);
        Assert.Equal(5, value.TotalEmployeeCount);
        Assert.Equal(12345, value.TotalStorageBytes);
        Assert.Equal(3, value.StorageFileCount);

        Assert.NotNull(value.Settings);
        Assert.Equal(settings.TimeZone, value.Settings!.TimeZone);
        Assert.Equal(settings.Locale, value.Settings.Locale);
        Assert.Equal(settings.WorkingDays, value.Settings.WorkingDays);
        Assert.Equal(settings.HoursPerDay, value.Settings.HoursPerDay);
        Assert.Equal(settings.LeaveYearStartMonth, value.Settings.LeaveYearStartMonth);
        Assert.Equal(settings.DefaultHolidayAllowance, value.Settings.DefaultHolidayAllowance);
        Assert.Equal(settings.ProbationMonths, value.Settings.ProbationMonths);
        Assert.Equal(settings.EmployeeNumberMode, value.Settings.EmployeeNumberMode);
        Assert.Equal(settings.EmployeeNumberPrefix, value.Settings.EmployeeNumberPrefix);
        Assert.Equal(settings.NextEmployeeNumber, value.Settings.NextEmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Maps_Company_With_No_Subscription_Row_To_None_Status_And_Null_Charge()
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
            new GetCustomerDetailsRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal("None", value.SubscriptionStatus);
        Assert.Null(value.TrialStartedAt);
        Assert.Null(value.TrialExpiresAt);
        Assert.Null(value.CurrentPeriodEnd);
        Assert.False(value.CancelAtPeriodEnd);
        Assert.Null(value.MonthlyCharge);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Settings_When_Company_Has_No_Settings_Row()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Unsettled Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new GetCustomerDetailsRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Settings);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_MonthlyCharge_When_Subscription_Is_Not_Active()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Trial Co", Now);
        context.Companies.Add(company);

        var subscription = CustomerSubscription.StartTrial(company.Id, Now, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            monthlyPriceGbp: 49m);

        var result = await handler.HandleAsync(
            new GetCustomerDetailsRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trial.ToString(), result.Value!.SubscriptionStatus);
        Assert.Null(result.Value.MonthlyCharge);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_MonthlyCharge_When_Subscription_Is_Canceled()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Canceled Co", Now);
        context.Companies.Add(company);

        var subscription = CustomerSubscription.StartTrial(company.Id, Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(1));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            monthlyPriceGbp: 49m);

        var result = await handler.HandleAsync(
            new GetCustomerDetailsRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Canceled.ToString(), result.Value!.SubscriptionStatus);
        Assert.Null(result.Value.MonthlyCharge);
    }

    private static GetCustomerDetailsHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakeEmployeeDirectoryReader? employeeDirectoryReader = null,
        FakeDocumentStorageReader? documentStorageReader = null,
        decimal monthlyPriceGbp = 49m)
    {
        return new GetCustomerDetailsHandler(
            context,
            currentUser,
            configuration,
            employeeDirectoryReader ?? new FakeEmployeeDirectoryReader(),
            Options.Create(new StripeOptions { MonthlyPriceGbp = monthlyPriceGbp }),
            documentStorageReader ?? new FakeDocumentStorageReader());
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
