using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.ScheduleCustomerDeletion;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class ScheduleCustomerDeletionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new ScheduleCustomerDeletionRequest { CompanyId = companyId, Reason = "Customer requested closure" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Subscription_Row_Exists()
    {
        await using var context = BuildContext();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new ScheduleCustomerDeletionRequest { CompanyId = Guid.NewGuid(), Reason = "Customer requested closure" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Deletion_Already_Executed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.ExecuteDeletion(Now.AddDays(31));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new ScheduleCustomerDeletionRequest { CompanyId = companyId, Reason = "Trying to re-schedule" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Schedules_Deletion_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new ScheduleCustomerDeletionRequest { CompanyId = companyId, Reason = "Customer requested closure", CountdownDays = 10 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(10), result.Value!.DeletionScheduledAt);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(Now.AddDays(10), persisted.DeletionScheduledAt);
        Assert.Equal(actorId, persisted.DeletionScheduledBy);
        Assert.True(persisted.HasPendingDeletion);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<CustomerDeletionScheduledAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal(Now.AddDays(10), auditEvent.DeletionScheduledAt);
        Assert.Equal("Customer requested closure", auditEvent.Reason);
    }

    [Fact]
    public async Task HandleAsync_Uses_DefaultCountdownDays_When_CountdownDays_Is_Omitted()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new ScheduleCustomerDeletionRequest { CompanyId = companyId, Reason = "Customer requested closure" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(ScheduleCustomerDeletionHandler.DefaultCountdownDays), result.Value!.DeletionScheduledAt);
    }

    private static ScheduleCustomerDeletionHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher)
    {
        return new ScheduleCustomerDeletionHandler(
            context,
            currentUser,
            configuration,
            new FakeClock(Now.UtcDateTime),
            auditEventPublisher);
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
