using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests.Jobs;

// Ticket 12 (P1): unit tests for InviteAcceptanceReconciliationJob, the recurring sweep that
// resolves InviteAcceptanceOperation rows left stuck SupabaseConfirmed — either because the
// invite behind them was cancelled while a Supabase account may have been created for it
// (Orphaned + audited), or because they simply fell behind reflecting that the invite was
// actually claimed (Completed, no audit).
[Collection("IdentityDatabase")]
public class InviteAcceptanceReconciliationJobTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    // NOTE: IdentityDatabaseFixture is a shared database across every test in the "IdentityDatabase"
    // collection (no per-test reset), so assertions here look for this test's own operation/invite
    // by Id within the results rather than asserting exact totals — other tests in the same
    // collection can leave their own rows behind.

    private static InviteAcceptanceReconciliationJob BuildJob(
        IdentityDbContext db, FakeAuditEventPublisher auditPublisher) =>
        new(db, Clock, auditPublisher, NullLogger<InviteAcceptanceReconciliationJob>.Instance);

    private async Task<(InviteAcceptanceOperation Operation, UserInvite Invite)> SeedStaleOperationAsync(
        Action<UserInvite>? configureInvite = null,
        DateTimeOffset? updatedAt = null,
        bool seedInvite = true)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var email = $"reconcile.{Guid.NewGuid():N}@example.com";

        var invite = UserInvite.Create(employeeId, companyId, email, CreatedAt);
        configureInvite?.Invoke(invite);

        var operation = InviteAcceptanceOperation.CreatePending(
            Guid.NewGuid(), invite.Id, companyId, employeeId, email, CreatedAt);
        operation.MarkSupabaseConfirmed(Guid.NewGuid(), CreatedAt.AddMinutes(1));

        await using var db = fixture.BuildContext();
        if (seedInvite)
            db.UserInvites.Add(invite);
        db.InviteAcceptanceOperations.Add(operation);
        await db.SaveChangesAsync();

        // Force UpdatedAt to the desired staleness — the domain method always stamps "now" at the
        // time it's called, so backdate it directly via EF for stale-scenario tests.
        db.Entry(operation).Property("UpdatedAt").CurrentValue =
            updatedAt ?? Now.AddMinutes(-20);
        await db.SaveChangesAsync();

        return (operation, invite);
    }

    [Fact]
    public async Task ExecuteAsync_Orphans_Stale_Operation_Whose_Invite_Is_Cancelled_And_Publishes_Audit_Event()
    {
        var (operation, invite) = await SeedStaleOperationAsync(i => i.Cancel(CreatedAt.AddMinutes(2)));

        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        await BuildJob(db, auditPublisher).ExecuteAsync();

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusOrphaned, reloaded.Status);

        var published = Assert.Single(auditPublisher.PublishedEvents
            .OfType<InviteAcceptanceOrphanedByCancellationAuditEvent>()
            .Where(e => e.InviteId == invite.Id));
        Assert.Equal(operation.CompanyId, published.CompanyId);
        Assert.Equal(operation.EmployeeId, published.EmployeeId);
        Assert.Equal(operation.SupabaseAuthUserId, published.SupabaseAuthUserId);
    }

    [Fact]
    public async Task ExecuteAsync_Orphans_Stale_Operation_Whose_Invite_No_Longer_Exists_And_Publishes_Audit_Event()
    {
        var (operation, invite) = await SeedStaleOperationAsync(seedInvite: false);

        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        await BuildJob(db, auditPublisher).ExecuteAsync();

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusOrphaned, reloaded.Status);

        Assert.Single(auditPublisher.PublishedEvents
            .OfType<InviteAcceptanceOrphanedByCancellationAuditEvent>()
            .Where(e => e.InviteId == invite.Id));
    }

    [Fact]
    public async Task ExecuteAsync_Completes_Stale_Operation_Whose_Invite_Is_Claimed_Without_Auditing()
    {
        var (operation, invite) = await SeedStaleOperationAsync(i => i.Claim(CreatedAt.AddMinutes(2)));

        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        await BuildJob(db, auditPublisher).ExecuteAsync();

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusCompleted, reloaded.Status);

        Assert.DoesNotContain(auditPublisher.PublishedEvents
            .OfType<InviteAcceptanceOrphanedByCancellationAuditEvent>(),
            e => e.InviteId == invite.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_Stale_Operation_Untouched_When_Invite_Neither_Cancelled_Nor_Claimed()
    {
        var (operation, _) = await SeedStaleOperationAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        await BuildJob(db, auditPublisher).ExecuteAsync();

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusSupabaseConfirmed, reloaded.Status);

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Sweep_Fresh_Operation_Even_When_Invite_Is_Cancelled()
    {
        var (operation, _) = await SeedStaleOperationAsync(
            i => i.Cancel(CreatedAt.AddMinutes(2)),
            updatedAt: Now.AddMinutes(-2));

        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        await BuildJob(db, auditPublisher).ExecuteAsync();

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusSupabaseConfirmed, reloaded.Status); // untouched

        Assert.Empty(auditPublisher.PublishedEvents);
    }
}
