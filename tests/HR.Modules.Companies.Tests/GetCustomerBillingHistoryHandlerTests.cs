using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCustomerBillingHistory;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

public class GetCustomerBillingHistoryHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Some Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakeStripeGateway(),
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(company.Id), CancellationToken.None);

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
            BuildConfiguration("admin@example.com"),
            new FakeStripeGateway(),
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_StripeNotConfigured_When_No_SecretKey()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "No Stripe Key Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakeStripeGateway(),
            secretKey: "");

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.False(value.StripeConfigured);
        Assert.Empty(value.Invoices);
    }

    [Fact]
    public async Task HandleAsync_Returns_NoStripeCustomer_When_Subscription_Has_No_StripeCustomerId()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "No Stripe Customer Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        var subscription = CustomerSubscription.StartTrial(company.Id, new DateTimeOffset(Now), 30);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakeStripeGateway(),
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.True(value.StripeConfigured);
        Assert.False(value.HasStripeCustomer);
        Assert.Empty(value.Invoices);
    }

    [Fact]
    public async Task HandleAsync_Returns_Real_Invoices_From_Stripe_Gateway_When_Customer_Linked()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Linked Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        var subscription = CustomerSubscription.StartTrial(company.Id, new DateTimeOffset(Now), 30);
        subscription.ActivateSubscription("cus_123", "sub_123", "price_123", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            InvoicesToReturn =
            [
                new StripeInvoiceSummary(
                    "in_001",
                    new DateTimeOffset(Now),
                    49m,
                    "gbp",
                    "paid",
                    new DateTimeOffset(Now),
                    "https://invoice.stripe.com/in_001"),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.True(value.StripeConfigured);
        Assert.True(value.HasStripeCustomer);
        Assert.Single(value.Invoices);
        Assert.Equal("in_001", value.Invoices[0].StripeInvoiceId);
        Assert.Equal(49m, value.Invoices[0].Amount);
        Assert.Equal("paid", value.Invoices[0].PaymentStatus);
        Assert.Equal("https://invoice.stripe.com/in_001", value.Invoices[0].HostedInvoiceUrl);
        Assert.Equal("cus_123", stripeGateway.LastListInvoicesStripeCustomerId);
    }

    [Fact]
    public async Task HandleAsync_Estimates_EmployeeCount_From_Closest_Snapshot_At_Or_Before_Invoice_Date()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Snapshot Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        var subscription = CustomerSubscription.StartTrial(company.Id, new DateTimeOffset(Now), 30);
        subscription.ActivateSubscription("cus_456", "sub_456", "price_456", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);

        context.CustomerBillingSnapshots.Add(CustomerBillingSnapshot.Create(
            company.Id, new DateTimeOffset(Now.AddDays(-10)), 3, 0, 0, 3, 10m, 0m, 30m));
        context.CustomerBillingSnapshots.Add(CustomerBillingSnapshot.Create(
            company.Id, new DateTimeOffset(Now.AddDays(-2)), 5, 0, 0, 5, 10m, 0m, 50m));
        context.CustomerBillingSnapshots.Add(CustomerBillingSnapshot.Create(
            company.Id, new DateTimeOffset(Now.AddDays(5)), 8, 0, 0, 8, 10m, 0m, 80m));
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            InvoicesToReturn =
            [
                new StripeInvoiceSummary(
                    "in_002",
                    new DateTimeOffset(Now),
                    50m,
                    "gbp",
                    "paid",
                    new DateTimeOffset(Now),
                    "https://invoice.stripe.com/in_002"),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetCustomerBillingHistoryRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Invoices[0].EstimatedEmployeeCount);
    }

    private static GetCustomerBillingHistoryHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakeStripeGateway stripeGateway,
        string secretKey)
    {
        return new GetCustomerBillingHistoryHandler(
            context,
            currentUser,
            configuration,
            stripeGateway,
            Options.Create(new StripeOptions { SecretKey = secretKey }));
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
