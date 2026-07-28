using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ResendInvite;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ResendInviteHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private ResendInviteHandler BuildHandler(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, new FakeEmailSender(), new FakeInviteLinkBuilder(), auditPublisher);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Invite_Missing()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new ResendInviteRequest { CompanyId = Guid.NewGuid(), InviteId = Guid.NewGuid() },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Invite_Already_Claimed()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "claimed@test.com", Now);
        invite.Claim(Now);

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new ResendInviteRequest { CompanyId = companyId, InviteId = invite.Id },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Invite_Already_Cancelled()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "cancelled@test.com", Now);
        invite.Cancel(Now);

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new ResendInviteRequest { CompanyId = companyId, InviteId = invite.Id },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Regenerates_Token_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "pending@test.com", Now);
        var originalToken = invite.Token;

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new ResendInviteRequest { CompanyId = companyId, InviteId = invite.Id },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.UserInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.NotEqual(originalToken, reloaded.Token);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserInviteResentAuditEvent);
    }
}
