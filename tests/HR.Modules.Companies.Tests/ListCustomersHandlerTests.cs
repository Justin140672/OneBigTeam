using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.ListCustomers;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

public class ListCustomersHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(new ListCustomersRequest(null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Companies_With_Correct_Mapping_When_No_Search_Term()
    {
        await using var context = BuildContext();

        var activeCompany = Company.Create(Guid.NewGuid(), "Active Co", Now);
        var activeSubscription = CustomerSubscription.StartTrial(activeCompany.Id, Now, trialLengthDays: 14);
        activeSubscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);

        var trialCompany = Company.Create(Guid.NewGuid(), "Trial Co", Now);
        var trialSubscription = CustomerSubscription.StartTrial(trialCompany.Id, Now, trialLengthDays: 14);

        context.Companies.AddRange(activeCompany, trialCompany);
        context.CustomerSubscriptions.AddRange(activeSubscription, trialSubscription);
        await context.SaveChangesAsync();

        var employeeReader = new FakeEmployeeDirectoryReader { TotalCountToReturn = 7 };
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            employeeReader,
            monthlyPriceGbp: 49m);

        var result = await handler.HandleAsync(new ListCustomersRequest(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Customers.Count);

        var activeItem = result.Value.Customers.Single(c => c.CompanyId == activeCompany.Id);
        Assert.Equal("Active Co", activeItem.CompanyName);
        Assert.Equal(SubscriptionStatus.Active.ToString(), activeItem.SubscriptionStatus);
        Assert.Equal(7, activeItem.CurrentEmployeeCount);
        Assert.Equal(49m, activeItem.MonthlyCharge);
        Assert.Equal(activeSubscription.TrialExpiresAt, activeItem.TrialEndsAt);
        Assert.Equal(activeCompany.CreatedAt, activeItem.CreatedAt);

        var trialItem = result.Value.Customers.Single(c => c.CompanyId == trialCompany.Id);
        Assert.Equal(SubscriptionStatus.Trial.ToString(), trialItem.SubscriptionStatus);
        Assert.Null(trialItem.MonthlyCharge);
        Assert.Equal(trialSubscription.TrialExpiresAt, trialItem.TrialEndsAt);
    }

    [Fact]
    public async Task HandleAsync_Maps_Company_With_No_Subscription_Row_To_None_Status()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "No Sub Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(new ListCustomersRequest(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Customers);
        Assert.Equal("None", item.SubscriptionStatus);
        Assert.Null(item.MonthlyCharge);
        Assert.Null(item.TrialEndsAt);
    }

    [Fact]
    public async Task HandleAsync_Search_By_Guid_Filters_To_Matching_Company_Only()
    {
        await using var context = BuildContext();
        var target = Company.Create(Guid.NewGuid(), "Target Co", Now);
        var other = Company.Create(Guid.NewGuid(), "Other Co", Now);
        context.Companies.AddRange(target, other);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(
            new ListCustomersRequest(target.Id.ToString()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Customers);
        Assert.Equal(target.Id, item.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Search_By_Name_Filters_Case_Insensitively()
    {
        await using var context = BuildContext();
        var acme = Company.Create(Guid.NewGuid(), "Acme Corp", Now);
        var other = Company.Create(Guid.NewGuid(), "Other Co", Now);
        context.Companies.AddRange(acme, other);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(new ListCustomersRequest("acme"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Customers);
        Assert.Equal(acme.Id, item.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Search_By_Email_Includes_Companies_Matched_By_EmailSearchReader()
    {
        await using var context = BuildContext();
        var matchedByEmail = Company.Create(Guid.NewGuid(), "Zephyr Ltd", Now);
        var unrelated = Company.Create(Guid.NewGuid(), "Unrelated Ltd", Now);
        context.Companies.AddRange(matchedByEmail, unrelated);
        await context.SaveChangesAsync();

        var emailReader = new FakeCompanyUserEmailSearchReader
        {
            CompanyIdsToReturn = [matchedByEmail.Id],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            emailSearchReader: emailReader);

        var result = await handler.HandleAsync(
            new ListCustomersRequest("someone@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Customers);
        Assert.Equal(matchedByEmail.Id, item.CompanyId);
        Assert.Equal("someone@example.com", emailReader.LastSearchTerm);
    }

    private static ListCustomersHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakeEmployeeDirectoryReader? employeeDirectoryReader = null,
        FakeCompanyUserEmailSearchReader? emailSearchReader = null,
        decimal monthlyPriceGbp = 49m)
    {
        return new ListCustomersHandler(
            context,
            currentUser,
            configuration,
            employeeDirectoryReader ?? new FakeEmployeeDirectoryReader(),
            emailSearchReader ?? new FakeCompanyUserEmailSearchReader(),
            Options.Create(new StripeOptions { MonthlyPriceGbp = monthlyPriceGbp }));
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
