using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CancelInvite;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class CancelInviteHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private CancelInviteHandler BuildHandler(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, auditPublisher);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Invite_Missing()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new CancelInviteRequest { CompanyId = Guid.NewGuid(), InviteId = Guid.NewGuid() },
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
            new CancelInviteRequest { CompanyId = companyId, InviteId = invite.Id },
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
            new CancelInviteRequest { CompanyId = companyId, InviteId = invite.Id },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Cancels_Invite_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "pending@test.com", Now);

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new CancelInviteRequest { CompanyId = companyId, InviteId = invite.Id },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.UserInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.True(reloaded.IsCancelled);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserInviteCancelledAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Cancel_Increments_Invite_Version()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "version@test.com", Now);

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new CancelInviteRequest { CompanyId = companyId, InviteId = invite.Id },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.UserInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.Equal(2, reloaded.Version);
    }

    // Ticket 2 (P1): pins the optimistic-concurrency guard directly — CancelInvite and AcceptInvite
    // both pin the Version they read before saving (see Handler.cs / AcceptInvite/Endpoint.cs), so
    // whichever writes second against a stale Version must fail rather than silently overwrite the
    // other's outcome. This simulates that race at the DbContext level: context B (standing in for
    // a concurrent AcceptInvite claim) saves first and bumps Version to 2, then CancelInvite's
    // handler — still holding the stale Version == 1 it read via context A — saves and must be
    // rejected as a concurrency conflict, leaving the claim intact.
    [Fact]
    public async Task HandleAsync_Stale_Version_Returns_Concurrency_Failure_When_Invite_Claimed_Concurrently()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "race@test.com", Now);

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        // Context A loads and tracks the row while Version == 1 (mirrors the handler's own read).
        await using var ctxA = fixture.BuildContext();
        var trackedA = await ctxA.UserInvites.FirstAsync(i => i.Id == invite.Id);
        var expectedVersionA = trackedA.Version;

        // Context B wins the race: a concurrent AcceptInvite claims the invite first, bumping
        // Version to 2.
        await using (var ctxB = fixture.BuildContext())
        {
            var trackedB = await ctxB.UserInvites.FirstAsync(i => i.Id == invite.Id);
            trackedB.Claim(Now);
            var saveResult = await ctxB.SaveChangesWithConcurrencyAsync(
                trackedB, trackedB.Version, "unexpected", CancellationToken.None);
            Assert.True(saveResult.IsSuccess);
        }

        // CancelInvite's handler now saves against context A with the now-stale Version == 1.
        trackedA.Cancel(Now);
        var result = await ctxA.SaveChangesWithConcurrencyAsync(
            trackedA, expectedVersionA, "This invitation was already accepted and can no longer be cancelled.", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = fixture.BuildContext();
        var reloaded = await verify.UserInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.True(reloaded.IsClaimed);
        Assert.False(reloaded.IsCancelled);
        Assert.Equal(2, reloaded.Version);
    }
}
