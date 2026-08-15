using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetFailedPayments;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests;

public class GetFailedPaymentsHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakeStripeGateway(),
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_StripeNotConfigured_When_No_SecretKey()
    {
        await using var context = BuildContext();

        var stripeGateway = new FakeStripeGateway
        {
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary(
                    "in_001", "cus_1", new DateTimeOffset(Now), 100m, "gbp", "open", null, null),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.False(value.StripeConfigured);
        Assert.Empty(value.FailedPayments);
        Assert.Empty(stripeGateway.GetMostRecentPaidInvoiceCalls);
    }

    [Fact]
    public async Task HandleAsync_Maps_And_Joins_FailedInvoices_To_Local_Company_And_Subscription()
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
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary(
                    "in_001",
                    "cus_123",
                    new DateTimeOffset(Now),
                    99.50m,
                    "gbp",
                    "open",
                    new DateTimeOffset(Now.AddDays(3)),
                    "https://invoice.stripe.com/in_001"),
            ],
            MostRecentPaidInvoiceByStripeCustomerId = new Dictionary<string, StripeInvoiceSummary?>
            {
                ["cus_123"] = new StripeInvoiceSummary(
                    "in_paid_001", new DateTimeOffset(Now.AddDays(-30)), 49m, "gbp", "paid", new DateTimeOffset(Now.AddDays(-30)), null),
            },
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.True(value.StripeConfigured);
        var dto = Assert.Single(value.FailedPayments);
        Assert.Equal(company.Id, dto.CompanyId);
        Assert.Equal("Linked Co", dto.CompanyName);
        Assert.Equal(subscription.Status.ToString(), dto.SubscriptionStatus);
        Assert.Equal("in_001", dto.StripeInvoiceId);
        Assert.Equal("open", dto.InvoiceStatus);
        Assert.Equal(99.50m, dto.OutstandingAmount);
        Assert.Equal("gbp", dto.Currency);
        Assert.Equal(new DateTimeOffset(Now.AddDays(3)), dto.RetryScheduledAt);
        Assert.Equal(new DateTimeOffset(Now.AddDays(-30)), dto.LastSuccessfulPaymentAt);
        Assert.Equal(49m, dto.LastSuccessfulPaymentAmount);
        Assert.Equal("https://invoice.stripe.com/in_001", dto.HostedInvoiceUrl);
        Assert.Equal(["cus_123"], stripeGateway.GetMostRecentPaidInvoiceCalls);
    }

    [Fact]
    public async Task HandleAsync_Leaves_LastSuccessfulPayment_Null_When_Gateway_Returns_Null()
    {
        await using var context = BuildContext();

        var company = Company.Create(Guid.NewGuid(), "Never Paid Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        var subscription = CustomerSubscription.StartTrial(company.Id, new DateTimeOffset(Now), 30);
        subscription.ActivateSubscription("cus_999", "sub_999", "price_999", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary(
                    "in_002", "cus_999", new DateTimeOffset(Now), 10m, "gbp", "uncollectible", null, null),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, null), CancellationToken.None);

        var dto = Assert.Single(result.Value!.FailedPayments);
        Assert.Null(dto.LastSuccessfulPaymentAt);
        Assert.Null(dto.LastSuccessfulPaymentAmount);
    }

    [Fact]
    public async Task HandleAsync_Applies_StatusFilter()
    {
        await using var context = BuildContext();

        var openCo = Company.Create(Guid.NewGuid(), "Open Co", new DateTimeOffset(Now));
        var uncollectibleCo = Company.Create(Guid.NewGuid(), "Uncollectible Co", new DateTimeOffset(Now));
        context.Companies.AddRange(openCo, uncollectibleCo);

        var openSub = CustomerSubscription.StartTrial(openCo.Id, new DateTimeOffset(Now), 30);
        openSub.ActivateSubscription("cus_open", "sub_open", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        var uncollectibleSub = CustomerSubscription.StartTrial(uncollectibleCo.Id, new DateTimeOffset(Now), 30);
        uncollectibleSub.ActivateSubscription("cus_uncollectible", "sub_uncollectible", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.AddRange(openSub, uncollectibleSub);
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary("in_open", "cus_open", new DateTimeOffset(Now), 10m, "gbp", "open", null, null),
                new FailedInvoiceSummary("in_uncollectible", "cus_uncollectible", new DateTimeOffset(Now), 20m, "gbp", "uncollectible", null, null),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, "open"), CancellationToken.None);

        var dto = Assert.Single(result.Value!.FailedPayments);
        Assert.Equal("in_open", dto.StripeInvoiceId);
    }

    [Fact]
    public async Task HandleAsync_Applies_Search_By_CompanyName()
    {
        await using var context = BuildContext();

        var matching = Company.Create(Guid.NewGuid(), "Distinctive Widgets Ltd", new DateTimeOffset(Now));
        var nonMatching = Company.Create(Guid.NewGuid(), "Other Corp", new DateTimeOffset(Now));
        context.Companies.AddRange(matching, nonMatching);

        var matchingSub = CustomerSubscription.StartTrial(matching.Id, new DateTimeOffset(Now), 30);
        matchingSub.ActivateSubscription("cus_match", "sub_match", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        var nonMatchingSub = CustomerSubscription.StartTrial(nonMatching.Id, new DateTimeOffset(Now), 30);
        nonMatchingSub.ActivateSubscription("cus_nomatch", "sub_nomatch", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.AddRange(matchingSub, nonMatchingSub);
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary("in_match", "cus_match", new DateTimeOffset(Now), 10m, "gbp", "open", null, null),
                new FailedInvoiceSummary("in_nomatch", "cus_nomatch", new DateTimeOffset(Now), 20m, "gbp", "open", null, null),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest("distinctive", null), CancellationToken.None);

        var dto = Assert.Single(result.Value!.FailedPayments);
        Assert.Equal("in_match", dto.StripeInvoiceId);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Invoices_With_No_Matching_Local_Subscription()
    {
        await using var context = BuildContext();

        var company = Company.Create(Guid.NewGuid(), "Known Co", new DateTimeOffset(Now));
        context.Companies.Add(company);
        var subscription = CustomerSubscription.StartTrial(company.Id, new DateTimeOffset(Now), 30);
        subscription.ActivateSubscription("cus_known", "sub_known", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary("in_known", "cus_known", new DateTimeOffset(Now), 10m, "gbp", "open", null, null),
                new FailedInvoiceSummary("in_orphan", "cus_orphan", new DateTimeOffset(Now), 20m, "gbp", "open", null, null),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, null), CancellationToken.None);

        var dto = Assert.Single(result.Value!.FailedPayments);
        Assert.Equal("in_known", dto.StripeInvoiceId);
    }

    [Fact]
    public async Task HandleAsync_Orders_Results_By_InvoiceDate_Descending()
    {
        await using var context = BuildContext();

        var older = Company.Create(Guid.NewGuid(), "Older Co", new DateTimeOffset(Now));
        var newer = Company.Create(Guid.NewGuid(), "Newer Co", new DateTimeOffset(Now));
        context.Companies.AddRange(older, newer);

        var olderSub = CustomerSubscription.StartTrial(older.Id, new DateTimeOffset(Now), 30);
        olderSub.ActivateSubscription("cus_older", "sub_older", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        var newerSub = CustomerSubscription.StartTrial(newer.Id, new DateTimeOffset(Now), 30);
        newerSub.ActivateSubscription("cus_newer", "sub_newer", "price_x", new DateTimeOffset(Now).AddDays(30), new DateTimeOffset(Now));
        context.CustomerSubscriptions.AddRange(olderSub, newerSub);
        await context.SaveChangesAsync();

        var stripeGateway = new FakeStripeGateway
        {
            FailedInvoicesToReturn =
            [
                new FailedInvoiceSummary("in_older", "cus_older", new DateTimeOffset(Now.AddDays(-10)), 10m, "gbp", "open", null, null),
                new FailedInvoiceSummary("in_newer", "cus_newer", new DateTimeOffset(Now), 20m, "gbp", "open", null, null),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            stripeGateway,
            secretKey: "sk_test_123");

        var result = await handler.HandleAsync(
            new GetFailedPaymentsRequest(null, null), CancellationToken.None);

        var items = result.Value!.FailedPayments;
        Assert.Equal(2, items.Count);
        Assert.Equal("in_newer", items[0].StripeInvoiceId);
        Assert.Equal("in_older", items[1].StripeInvoiceId);
    }

    private static GetFailedPaymentsHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakeStripeGateway stripeGateway,
        string secretKey)
    {
        return new GetFailedPaymentsHandler(
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
