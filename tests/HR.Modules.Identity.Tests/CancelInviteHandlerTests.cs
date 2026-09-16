using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CancelInvite;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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

    // Ticket 24 (P1): the keyed-cancellation branch now routes through
    // SaveIdempotentWithConcurrencyAsync instead of plain SaveIdempotentAsync, so the same
    // AcceptInvite-races-CancelInvite scenario above must also be exercised WITH an Idempotency-Key
    // present - asserting both that the result is a controlled concurrency failure (never an
    // unhandled DbUpdateConcurrencyException) and that a losing keyed attempt commits NO
    // IdempotencyRecord for that key (see below for the corresponding "key is safely reusable"
    // follow-up).
    [Fact]
    public async Task HandleAsync_Keyed_Stale_Version_Returns_Concurrency_Failure_And_Commits_No_Idempotency_Record()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invite = UserInvite.Create(employeeId, companyId, "keyed-race@test.com", Now);

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var idempotencyKey = $"cancel-race-{Guid.NewGuid():N}";

        // Context A loads and tracks the row while Version == 1 (mirrors the keyed handler's own
        // read, held stale exactly like the unkeyed race test above).
        await using var ctxA = fixture.BuildContext();
        var trackedA = await ctxA.UserInvites.FirstAsync(i => i.Id == invite.Id);
        var expectedVersionA = trackedA.Version;

        // A concurrent AcceptInvite wins the race first, claiming the invite and bumping Version.
        await using (var ctxB = fixture.BuildContext())
        {
            var trackedB = await ctxB.UserInvites.FirstAsync(i => i.Id == invite.Id);
            trackedB.Claim(Now);
            var saveResult = await ctxB.SaveChangesWithConcurrencyAsync(
                trackedB, trackedB.Version, "unexpected", CancellationToken.None);
            Assert.True(saveResult.IsSuccess);
        }

        // Context A now applies the cancel and saves via the exact same
        // SaveIdempotentWithConcurrencyAsync call CancelInviteHandler's keyed branch uses (see
        // Handler.cs), against the now-stale Version == 1 it read before context B's claim.
        trackedA.Cancel(Now);
        var request = new CancelInviteRequest { CompanyId = companyId, InviteId = invite.Id };
        var scope = new IdempotencyScope(nameof(CancelInviteHandler), companyId, Guid.Empty);
        var fingerprint = DbContextIdempotencyExtensions.Fingerprint(request);
        var response = new CancelInviteResponse(trackedA.Id);

        var outcome = await ctxA.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, UserInvite, CancelInviteResponse>(
            ctxA.IdempotencyRecords, trackedA, expectedVersionA, scope, idempotencyKey, fingerprint,
            StatusCodes.Status200OK, response, new DateTimeOffset(Now.UtcDateTime, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(IdempotencyOutcomeKind.ConcurrencyConflict, outcome.Kind);

        await using var verify = fixture.BuildContext();
        var reloaded = await verify.UserInvites.FirstAsync(i => i.Id == invite.Id);
        Assert.True(reloaded.IsClaimed);
        Assert.False(reloaded.IsCancelled);

        // Critically, the losing keyed attempt must not have committed an idempotency record - a
        // spurious committed record for this key would cause a subsequent retry with the SAME key to
        // wrongly replay this failure instead of being re-evaluated against fresh state.
        var recordExists = await verify.IdempotencyRecords.AnyAsync(r => r.Key == idempotencyKey);
        Assert.False(recordExists);
    }

    // Ticket 24 (P1) acceptance criterion: "the same cancellation key can be reused after reload when
    // the previous attempt did not commit." Simulates a caller who: (1) fires a keyed cancellation
    // that loses to a concurrent AcceptInvite (as above, so no record is committed for the key), then
    // (2) reloads and retries the SAME key against a DIFFERENT, genuinely-cancellable invite - proving
    // the key was never poisoned by the earlier conflict.
    [Fact]
    public async Task HandleAsync_Keyed_Cancellation_Key_Can_Be_Reused_After_Failed_Conflicting_Attempt()
    {
        var companyId = Guid.NewGuid();
        var idempotencyKey = $"cancel-reuse-{Guid.NewGuid():N}";

        // First invite: loses the race to a concurrent claim, as above.
        var employeeId1 = Guid.NewGuid();
        var invite1 = UserInvite.Create(employeeId1, companyId, "reuse-first@test.com", Now);
        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite1);
            await db.SaveChangesAsync();
        }

        await using var ctxA1 = fixture.BuildContext();
        var trackedA1 = await ctxA1.UserInvites.FirstAsync(i => i.Id == invite1.Id);
        var expectedVersionA1 = trackedA1.Version;

        await using (var ctxB = fixture.BuildContext())
        {
            var trackedB = await ctxB.UserInvites.FirstAsync(i => i.Id == invite1.Id);
            trackedB.Claim(Now);
            var saveResult = await ctxB.SaveChangesWithConcurrencyAsync(
                trackedB, trackedB.Version, "unexpected", CancellationToken.None);
            Assert.True(saveResult.IsSuccess);
        }

        // The first (losing) attempt saves via the same SaveIdempotentWithConcurrencyAsync call
        // CancelInviteHandler's keyed branch uses, against the now-stale version.
        trackedA1.Cancel(Now);
        var firstRequest = new CancelInviteRequest { CompanyId = companyId, InviteId = invite1.Id };
        var firstScope = new IdempotencyScope(nameof(CancelInviteHandler), companyId, Guid.Empty);
        var firstFingerprint = DbContextIdempotencyExtensions.Fingerprint(firstRequest);
        var firstResponse = new CancelInviteResponse(trackedA1.Id);

        var firstOutcome = await ctxA1.SaveIdempotentWithConcurrencyAsync<IdempotencyRecord, UserInvite, CancelInviteResponse>(
            ctxA1.IdempotencyRecords, trackedA1, expectedVersionA1, firstScope, idempotencyKey, firstFingerprint,
            StatusCodes.Status200OK, firstResponse, new DateTimeOffset(Now.UtcDateTime, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(IdempotencyOutcomeKind.ConcurrencyConflict, firstOutcome.Kind);

        await using (var verify = fixture.BuildContext())
        {
            Assert.False(await verify.IdempotencyRecords.AnyAsync(r => r.Key == idempotencyKey));
        }

        // Second invite: a genuinely still-pending, cancellable invite - the "reload and retry" the
        // caller performs after the first attempt's conflict.
        var employeeId2 = Guid.NewGuid();
        var invite2 = UserInvite.Create(employeeId2, companyId, "reuse-second@test.com", Now);
        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(invite2);
            await db.SaveChangesAsync();
        }

        var retryHandler = BuildHandler(new FakeAuditEventPublisher());
        var retryResult = await retryHandler.HandleAsync(
            new CancelInviteRequest { CompanyId = companyId, InviteId = invite2.Id, IdempotencyKey = idempotencyKey },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(retryResult.IsSuccess);

        await using var verify2 = fixture.BuildContext();
        var reloadedInvite2 = await verify2.UserInvites.FirstAsync(i => i.Id == invite2.Id);
        Assert.True(reloadedInvite2.IsCancelled);

        var recordCount = await verify2.IdempotencyRecords.CountAsync(r => r.Key == idempotencyKey);
        Assert.Equal(1, recordCount);
    }
}
