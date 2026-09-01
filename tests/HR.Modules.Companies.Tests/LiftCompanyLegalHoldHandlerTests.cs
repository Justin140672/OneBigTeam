using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.LiftCompanyLegalHold;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class LiftCompanyLegalHoldHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var companyId = await SeedHeldAsync(context);

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new LiftCompanyLegalHoldRequest { CompanyId = companyId, Reason = "Matter resolved" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(publisher.Published);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(persisted.IsUnderLegalHold);
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
            new LiftCompanyLegalHoldRequest { CompanyId = Guid.NewGuid(), Reason = "Matter resolved" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Not_Currently_Held()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14));
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new LiftCompanyLegalHoldRequest { CompanyId = companyId, Reason = "Matter resolved" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Lifts_Hold_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = await SeedHeldAsync(context);

        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new LiftCompanyLegalHoldRequest { CompanyId = companyId, Reason = "Matter resolved" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.False(persisted.IsUnderLegalHold);
        Assert.Null(persisted.LegalHoldReason);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<CompanyLegalHoldLiftedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
    }

    private static async Task<Guid> SeedHeldAsync(CompaniesDbContext context)
    {
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);
        subscription.PlaceLegalHold(Guid.NewGuid(), "Initial litigation hold", Now.AddDays(-1));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();
        return companyId;
    }

    private static LiftCompanyLegalHoldHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher)
        => new(context, currentUser, configuration, new FakeClock(Now.UtcDateTime), auditEventPublisher);

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var builder = new ConfigurationBuilder();
        var data = allowedEmails
            .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
            .ToArray();
        builder.AddInMemoryCollection(data);
        return builder.Build();
    }

    private static CompaniesDbContext BuildContext()
        => new(new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
