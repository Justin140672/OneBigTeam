using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Ticket 12 (P1) / Ticket 17 (P1): recovers <see cref="InviteAcceptanceOperation"/> rows abandoned
/// partway through AcceptInvite (see Features/AcceptInvite/Endpoint.cs). Covers every non-terminal
/// and terminal state explicitly:
///
///  - Pending, stale (ticket 17): the operation was persisted BEFORE calling Supabase, but the row
///    never advanced further. AcceptInvite only ever persists the SupabaseConfirmed transition
///    together with (and immediately followed, in the very same save, by) MarkCompleted — so a
///    process interrupted ANY time after the Supabase call succeeds but before that final save
///    commits leaves the operation looking exactly like a genuinely fresh, never-dispatched
///    attempt: still Pending in the database. The previous version of this job only ever scanned
///    SupabaseConfirmed rows, so this entire interruption window was invisible to reconciliation.
///    Resolved by asking Supabase directly (by email) whether an account exists that carries THIS
///    operation's own provisioning-correlation id (see InviteAcceptanceOperation remarks) — the
///    same proof-of-ownership check AcceptInvite itself uses on retry:
///     - No matching account -> nothing was ever created for it (or creation failed outright) ->
///       MarkCancelled (terminal, no external identity involved).
///     - A matching account exists -> MarkOrphaned (same terminal state and audit-for-manual-
///       cleanup contract as the SupabaseConfirmed branch below), recording the discovered id.
///    A non-cancelled, non-claimed Pending operation is left untouched — still a legitimate
///    candidate for AcceptInvite's own retry/resume path, not this job's concern.
///  - SupabaseConfirmed, stale: a confirmed Supabase Auth user was created, but local provisioning
///    (UserProfile creation, role assignment, invite claim) never completed. Same
///    cancelled-> Orphaned / claimed -> Completed outcomes as above, using the SupabaseAuthUserId
///    already recorded on the operation rather than re-resolving it.
///  - Completed / Orphaned / Cancelled: terminal, never re-examined by either query below.
///
/// Never modifies an external Supabase identity whose metadata doesn't carry this exact
/// operation's correlation id — a foreign or genuinely pre-existing account for the same email is
/// left completely untouched in every branch.
/// </summary>
internal sealed class InviteAcceptanceReconciliationJob(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ILogger<InviteAcceptanceReconciliationJob> logger)
{
    /// <summary>
    /// An operation younger than this is assumed to still be a normal in-flight AcceptInvite call
    /// (which completes in well under a second under healthy conditions, including its Supabase
    /// round-trip), not abandoned.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(15);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNow;
        var cutoff = now - StaleThreshold;

        var staleConfirmed = await db.InviteAcceptanceOperations
            .Where(o => o.Status == InviteAcceptanceOperation.StatusSupabaseConfirmed && o.UpdatedAt < cutoff)
            .ToListAsync();

        foreach (var operation in staleConfirmed)
        {
            await ReconcileSupabaseConfirmedAsync(operation, now);
        }

        var stalePending = await db.InviteAcceptanceOperations
            .Where(o => o.Status == InviteAcceptanceOperation.StatusPending && o.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var operation in stalePending)
        {
            await ReconcilePendingAsync(operation, now);
        }
    }

    private async Task ReconcileSupabaseConfirmedAsync(InviteAcceptanceOperation operation, DateTimeOffset now)
    {
        var invite = await db.UserInvites.SingleOrDefaultAsync(i => i.Id == operation.InviteId);

        if (invite is null || invite.IsCancelled)
        {
            operation.MarkOrphaned(now);

            logger.LogWarning(
                "InviteAcceptanceReconciliationJob: orphaning invite acceptance operation {OperationId} for invite {InviteId} (company {CompanyId}) — invite was cancelled/removed while a Supabase account (id {SupabaseAuthUserId}) may have been created for it.",
                operation.Id, operation.InviteId, operation.CompanyId, operation.SupabaseAuthUserId);

            // Ticket 17 (P1): persist the state transition BEFORE publishing the audit event — the
            // operation must never revert to "unrecorded" just because the audit publish step
            // fails/is interrupted (a durable EventId, see IdentityAudit.cs, additionally makes a
            // later re-publish attempt safe if this exact save-then-publish ordering is ever
            // followed up with a genuine retry of the publish step).
            await db.SaveChangesAsync();

            await auditEventPublisher.PublishAsync(
                new InviteAcceptanceOrphanedByCancellationAuditEvent(
                    operation.CompanyId, operation.InviteId, operation.EmployeeId,
                    operation.SupabaseAuthUserId, now),
                CancellationToken.None);

            return;
        }

        if (invite.IsClaimed)
        {
            // A completed AcceptInvite call (this exact retry, or a concurrent one) already
            // resolved this normally — just behind on reflecting it here.
            operation.MarkCompleted(now);
            await db.SaveChangesAsync();

            logger.LogInformation(
                "InviteAcceptanceReconciliationJob: operation {OperationId} for invite {InviteId} converged to Completed — invite was already claimed.",
                operation.Id, operation.InviteId);
        }
    }

    private async Task ReconcilePendingAsync(InviteAcceptanceOperation operation, DateTimeOffset now)
    {
        var invite = await db.UserInvites.SingleOrDefaultAsync(i => i.Id == operation.InviteId);

        if (invite is not null && invite.IsClaimed)
        {
            // A completed AcceptInvite call already resolved this normally — just behind on
            // reflecting it here (mirrors the SupabaseConfirmed branch).
            operation.MarkCompleted(now);
            await db.SaveChangesAsync();

            logger.LogInformation(
                "InviteAcceptanceReconciliationJob: pending operation {OperationId} for invite {InviteId} converged to Completed — invite was already claimed.",
                operation.Id, operation.InviteId);
            return;
        }

        if (invite is not null && !invite.IsCancelled)
        {
            // Neither cancelled nor claimed yet — still a legitimate candidate for AcceptInvite's
            // own retry/resume path (or a genuinely fresh attempt still in flight, just past the
            // stale-threshold under unusually slow conditions). Not this job's concern.
            return;
        }

        // Ticket 17 (P1): the invite was cancelled/removed while this operation was still Pending.
        // Unlike the SupabaseConfirmed branch, this operation was NEVER confirmed to have reached
        // Supabase successfully — it might have (crashed after CreateConfirmedUserAsync succeeded
        // but before MarkSupabaseConfirmed+MarkCompleted's shared save committed), or it might not
        // have (crashed before ever calling Supabase, or the call itself failed). Resolve this
        // exactly the way AcceptInvite's own retry path does: only trust a Supabase account that
        // carries THIS operation's own provisioning-correlation id as proof of ownership. A
        // pre-existing/foreign account for the same email — or no account at all — is never
        // touched.
        var resolved = await supabaseAuthGateway.GetUserMetadataByEmailAsync(operation.Email, CancellationToken.None);

        var provesThisOperationCreatedIt =
            resolved is not null
            && resolved.Value.Metadata.TryGetValue("provisioning_operation_id", out var correlationId)
            && correlationId == operation.Id.ToString();

        if (!provesThisOperationCreatedIt)
        {
            operation.MarkCancelled(now);

            logger.LogInformation(
                "InviteAcceptanceReconciliationJob: cancelling stale pending invite acceptance operation {OperationId} for invite {InviteId} (company {CompanyId}) — invite was cancelled/removed and no matching Supabase account was found.",
                operation.Id, operation.InviteId, operation.CompanyId);

            await db.SaveChangesAsync();

            await auditEventPublisher.PublishAsync(
                new InviteAcceptancePendingCancelledAuditEvent(
                    operation.CompanyId, operation.InviteId, operation.EmployeeId, now),
                CancellationToken.None);

            return;
        }

        operation.MarkOrphaned(resolved!.Value.UserId, now);

        logger.LogWarning(
            "InviteAcceptanceReconciliationJob: orphaning stale pending invite acceptance operation {OperationId} for invite {InviteId} (company {CompanyId}) — invite was cancelled/removed but a matching Supabase account (id {SupabaseAuthUserId}) was found for it.",
            operation.Id, operation.InviteId, operation.CompanyId, operation.SupabaseAuthUserId);

        await db.SaveChangesAsync();

        await auditEventPublisher.PublishAsync(
            new InviteAcceptanceOrphanedByCancellationAuditEvent(
                operation.CompanyId, operation.InviteId, operation.EmployeeId,
                operation.SupabaseAuthUserId, now),
            CancellationToken.None);
    }
}
