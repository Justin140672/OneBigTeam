using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.RevokeSupportSession;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class RevokeSupportSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new RevokeSupportSessionRequest(session.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Equal("unauthorized", Assert.IsType<SupportSessionRevocationRejectedAuditEvent>(Assert.Single(publisher.Published)).Outcome);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new RevokeSupportSessionRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal("not_found", Assert.IsType<SupportSessionRevocationRejectedAuditEvent>(Assert.Single(publisher.Published)).Outcome);
    }

    [Fact]
    public async Task HandleAsync_Revokes_Redeemed_Session_And_Publishes_Audit_Event()
    {
        await using var context = BuildContext();
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Redeem(Now.AddMinutes(1));
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new RevokeSupportSessionRequest(session.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await context.SupportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(persisted.RevokedAt);
        Assert.False(persisted.GrantsAccess(Now));
        Assert.IsType<SupportSessionRevokedAuditEvent>(Assert.Single(publisher.Published));
    }

    [Fact]
    public async Task HandleAsync_Revokes_Session_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SupportSession.Issue(companyId, Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var result = await handler.HandleAsync(
            new RevokeSupportSessionRequest(session.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(session.Id, result.Value!.SupportSessionId);
        Assert.Equal(Now, result.Value.RevokedAt);

        var persisted = await context.SupportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(Now, persisted.RevokedAt);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<SupportSessionRevokedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(session.Id, auditEvent.SupportSessionId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Second_Revoke_Fails_And_Audits_Rejected_Attempt()
    {
        await using var context = BuildContext();
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Redeem(Now.AddMinutes(1));
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher);

        var first = await handler.HandleAsync(new RevokeSupportSessionRequest(session.Id), CancellationToken.None);
        var second = await handler.HandleAsync(new RevokeSupportSessionRequest(session.Id), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal("already_revoked", Assert.IsType<SupportSessionRevocationRejectedAuditEvent>(publisher.Published[1]).Outcome);
    }

    private static RevokeSupportSessionHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher)
    {
        return new RevokeSupportSessionHandler(
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
