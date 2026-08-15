using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.ResumeCustomerService;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class ResumeCustomerServiceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.ForceReadOnly(Now);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new ResumeCustomerServiceRequest { CompanyId = companyId, Reason = "Investigation concluded" },
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
            new ResumeCustomerServiceRequest { CompanyId = Guid.NewGuid(), Reason = "Investigation concluded" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Not_Currently_Forced()
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
            new ResumeCustomerServiceRequest { CompanyId = companyId, Reason = "Investigation concluded" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Resumes_Service_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.ForceReadOnly(Now);
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
            new ResumeCustomerServiceRequest { CompanyId = companyId, Reason = "Dispute resolved, resuming service" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AdminForcedReadOnly);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.False(persisted.AdminForcedReadOnly);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<ServiceResumedByAdminAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal("Dispute resolved, resuming service", auditEvent.Reason);
    }

    private static ResumeCustomerServiceHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher)
    {
        return new ResumeCustomerServiceHandler(
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
