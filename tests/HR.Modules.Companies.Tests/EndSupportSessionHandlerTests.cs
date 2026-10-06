using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.EndSupportSession;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class EndSupportSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Revokes_Redeemed_Session_And_Audits()
    {
        await using var context = BuildContext();
        var session = await SeedRedeemedAsync(context);
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, new SupportUser(session.Id, session.CompanyId.ToString()), publisher);

        var result = await handler.HandleAsync(new EndSupportSessionRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await context.SupportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(persisted.RevokedAt);
        var audit = Assert.IsType<SupportSessionEndedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal("revoked", audit.Outcome);
    }

    [Fact]
    public async Task HandleAsync_Repeated_End_Fails_And_Audits_Attempt()
    {
        await using var context = BuildContext();
        var session = await SeedRedeemedAsync(context);
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, new SupportUser(session.Id, session.CompanyId.ToString()), publisher);

        await handler.HandleAsync(new EndSupportSessionRequest(), CancellationToken.None);
        var second = await handler.HandleAsync(new EndSupportSessionRequest(), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("already_revoked", Assert.IsType<SupportSessionEndedAuditEvent>(publisher.Published[1]).Outcome);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Non_Support_Caller()
    {
        await using var context = BuildContext();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, FakeCurrentUser.Authenticated(Guid.NewGuid().ToString()), publisher);

        var result = await handler.HandleAsync(new EndSupportSessionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Company_Mismatch_Without_Revoking()
    {
        await using var context = BuildContext();
        var session = await SeedRedeemedAsync(context);
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, new SupportUser(session.Id, Guid.NewGuid().ToString()), publisher);

        var result = await handler.HandleAsync(new EndSupportSessionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null((await context.SupportSessions.SingleAsync(s => s.Id == session.Id)).RevokedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Missing_Session()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new SupportUser(Guid.NewGuid(), Guid.NewGuid().ToString()), new CapturingAuditEventPublisher());

        var result = await handler.HandleAsync(new EndSupportSessionRequest(), CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
    }

    private static async Task<SupportSession> SeedRedeemedAsync(CompaniesDbContext context)
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Redeem(Now.AddMinutes(1));
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    private static EndSupportSessionHandler BuildHandler(
        CompaniesDbContext context, ICurrentUser user, CapturingAuditEventPublisher publisher) =>
        new(context, user, new FakeClock(Now.UtcDateTime.AddMinutes(2)), publisher);

    private static CompaniesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class SupportUser(Guid supportSessionId, string tenantId) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Email => "admin@example.com";
        public string? TenantId { get; } = tenantId;
        public bool IsAuthenticated => true;
        public bool IsSupportSession => true;
        public Guid? SupportSessionId { get; } = supportSessionId;
    }
}
