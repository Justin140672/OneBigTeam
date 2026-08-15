using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetDeletionQueue;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class GetDeletionQueueHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var handler = new GetDeletionQueueHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Companies_With_A_Scheduled_Deletion_Ordered_By_ScheduledAt_Descending()
    {
        await using var context = BuildContext();

        var pendingCompany = Company.Create(Guid.NewGuid(), "Pending Deletion Co", Now);
        var pendingSubscription = CustomerSubscription.StartTrial(pendingCompany.Id, Now, trialLengthDays: 14);
        pendingSubscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);

        var cancelledCompany = Company.Create(Guid.NewGuid(), "Cancelled Deletion Co", Now);
        var cancelledSubscription = CustomerSubscription.StartTrial(cancelledCompany.Id, Now, trialLengthDays: 14);
        cancelledSubscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(60), Now);
        cancelledSubscription.CancelScheduledDeletion(Now.AddDays(1));

        var notScheduledCompany = Company.Create(Guid.NewGuid(), "No Deletion Co", Now);
        var notScheduledSubscription = CustomerSubscription.StartTrial(notScheduledCompany.Id, Now, trialLengthDays: 14);

        context.Companies.AddRange(pendingCompany, cancelledCompany, notScheduledCompany);
        context.CustomerSubscriptions.AddRange(pendingSubscription, cancelledSubscription, notScheduledSubscription);
        await context.SaveChangesAsync();

        var handler = new GetDeletionQueueHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("Cancelled Deletion Co", result.Value.Items[0].CompanyName);
        Assert.Equal(Now.AddDays(60), result.Value.Items[0].ScheduledAt);
        Assert.NotNull(result.Value.Items[0].CancelledAt);
        Assert.Null(result.Value.Items[0].ExecutedAt);

        Assert.Equal("Pending Deletion Co", result.Value.Items[1].CompanyName);
        Assert.Equal(Now.AddDays(30), result.Value.Items[1].ScheduledAt);
        Assert.Null(result.Value.Items[1].CancelledAt);
        Assert.Null(result.Value.Items[1].ExecutedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Deletions_Scheduled()
    {
        await using var context = BuildContext();
        var handler = new GetDeletionQueueHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
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
