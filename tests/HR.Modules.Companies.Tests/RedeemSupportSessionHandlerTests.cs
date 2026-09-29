using System.Security.Cryptography;
using System.Text;

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.RedeemSupportSession;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class RedeemSupportSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeSupportSessionTokenIssuer : ISupportSessionTokenIssuer
    {
        public string IssueToken(Guid supportSessionId, Guid companyId, Guid adminUserId, string adminEmail, DateTimeOffset expiresAt)
            => $"fake-token:{supportSessionId}";
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Token_Does_Not_Match_Any_Session()
    {
        await using var context = BuildContext();
        var publisher = new CapturingAuditEventPublisher();
        var handler = new RedeemSupportSessionHandler(context, new FakeClock(Now.UtcDateTime), publisher, new FakeSupportSessionTokenIssuer());

        var result = await handler.HandleAsync(
            new RedeemSupportSessionRequest("garbage-token-that-does-not-exist"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Redeems_Session_Persists_And_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        const string token = "known-raw-token-value";
        var session = SupportSession.Issue(companyId, adminUserId, "admin@example.com", "reason", HashToken(token), Now);
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = new RedeemSupportSessionHandler(context, new FakeClock(Now.AddMinutes(1).UtcDateTime), publisher, new FakeSupportSessionTokenIssuer());

        var result = await handler.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(adminUserId, result.Value.IssuedByAdminUserId);
        Assert.Equal("admin@example.com", result.Value.IssuedByAdminEmail);
        Assert.Equal(Now.AddMinutes(1), result.Value.RedeemedAt);

        var persisted = await context.SupportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(Now.AddMinutes(1), persisted.RedeemedAt);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<SupportSessionRedeemedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(session.Id, auditEvent.SupportSessionId);
        Assert.Equal(adminUserId, auditEvent.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_On_Second_Redeem_Attempt()
    {
        await using var context = BuildContext();
        const string token = "known-raw-token-value";
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), Now);
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var firstHandler = new RedeemSupportSessionHandler(context, new FakeClock(Now.AddMinutes(1).UtcDateTime), publisher, new FakeSupportSessionTokenIssuer());
        var firstResult = await firstHandler.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondHandler = new RedeemSupportSessionHandler(context, new FakeClock(Now.AddMinutes(2).UtcDateTime), publisher, new FakeSupportSessionTokenIssuer());
        var secondResult = await secondHandler.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("validation", secondResult.Error.Code);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Session_Is_Revoked()
    {
        await using var context = BuildContext();
        const string token = "known-raw-token-value";
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), Now);
        session.Revoke(Now.AddMinutes(1));
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = new RedeemSupportSessionHandler(context, new FakeClock(Now.AddMinutes(2).UtcDateTime), publisher, new FakeSupportSessionTokenIssuer());

        var result = await handler.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Session_Is_Expired()
    {
        await using var context = BuildContext();
        const string token = "known-raw-token-value";
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), Now);
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = new RedeemSupportSessionHandler(context, new FakeClock(Now.AddMinutes(21).UtcDateTime), publisher, new FakeSupportSessionTokenIssuer());

        var result = await handler.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Only_One_Of_Two_Concurrent_Redemption_Attempts_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString("N");
        const string token = "known-raw-token-value";

        await using (var seedContext = BuildContext(dbName))
        {
            var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", HashToken(token), Now);
            seedContext.SupportSessions.Add(session);
            await seedContext.SaveChangesAsync();
        }

        await using var context1 = BuildContext(dbName);
        await using var context2 = BuildContext(dbName);

        await context1.SupportSessions.SingleAsync(s => s.TokenHash == HashToken(token));
        await context2.SupportSessions.SingleAsync(s => s.TokenHash == HashToken(token));

        var publisher1 = new CapturingAuditEventPublisher();
        var publisher2 = new CapturingAuditEventPublisher();
        var handler1 = new RedeemSupportSessionHandler(context1, new FakeClock(Now.AddMinutes(1).UtcDateTime), publisher1, new FakeSupportSessionTokenIssuer());
        var handler2 = new RedeemSupportSessionHandler(context2, new FakeClock(Now.AddMinutes(2).UtcDateTime), publisher2, new FakeSupportSessionTokenIssuer());

        var result1 = await handler1.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);
        var result2 = await handler2.HandleAsync(new RedeemSupportSessionRequest(token), CancellationToken.None);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsFailure);
        Assert.Equal("concurrency", result2.Error.Code);

        Assert.Single(publisher1.Published);
        Assert.Empty(publisher2.Published);

        await using var verifyContext = BuildContext(dbName);
        var persisted = await verifyContext.SupportSessions.SingleAsync(s => s.TokenHash == HashToken(token));
        Assert.Equal(Now.AddMinutes(1), persisted.RedeemedAt);
        Assert.Equal(2, persisted.Version);
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static CompaniesDbContext BuildContext() => BuildContext(Guid.NewGuid().ToString("N"));

    private static CompaniesDbContext BuildContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new CompaniesDbContext(options);
    }
}
