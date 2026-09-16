using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Ticket 12 (P1): recovers <see cref="InviteAcceptanceOperation"/> rows left in
/// <see cref="InviteAcceptanceOperation.StatusSupabaseConfirmed"/> — a confirmed Supabase Auth user
/// was created, but local provisioning (UserProfile creation, role assignment, invite claim) never
/// completed and the operation was never marked Completed.
///
/// Two distinct outcomes, both only decided after the operation has sat SupabaseConfirmed for a
/// while (never immediately — a normal AcceptInvite call is still mid-flight between those two
/// steps for a brief window):
///
///  - The invite was CANCELLED (e.g. an administrator revoked it) while provisioning was in
///    flight, or after Supabase succeeded but before AcceptInvite's own final save — there is no
///    remaining path to ever complete this operation normally. Marked Orphaned and audited so the
///    orphaned Supabase user id is visible for manual cleanup; this job never calls a
///    Supabase-account-deletion API itself (see IdentityAudit.cs remarks on the audit event).
///  - The invite was CLAIMED (a retried AcceptInvite call — or this exact scenario resolving
///    itself before this job ran — actually finished normally) — the operation is simply behind on
///    being marked Completed; converged forward rather than left dangling.
///
/// An invite that is neither cancelled nor claimed yet is left untouched — still a legitimate
/// candidate for AcceptInvite's own retry/resume path, not this job's concern.
/// </summary>
internal sealed class InviteAcceptanceReconciliationJob(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ILogger<InviteAcceptanceReconciliationJob> logger)
{
    /// <summary>
    /// A SupabaseConfirmed operation younger than this is assumed to still be a normal in-flight
    /// AcceptInvite call (which completes in well under a second under healthy conditions), not
    /// abandoned.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(15);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNow;
        var cutoff = now - StaleThreshold;

        var stale = await db.InviteAcceptanceOperations
            .Where(o => o.Status == InviteAcceptanceOperation.StatusSupabaseConfirmed && o.UpdatedAt < cutoff)
            .ToListAsync();

        if (stale.Count == 0)
            return;

        foreach (var operation in stale)
        {
            var invite = await db.UserInvites.SingleOrDefaultAsync(i => i.Id == operation.InviteId);

            if (invite is null || invite.IsCancelled)
            {
                operation.MarkOrphaned(now);

                logger.LogWarning(
                    "InviteAcceptanceReconciliationJob: orphaning invite acceptance operation {OperationId} for invite {InviteId} (company {CompanyId}) — invite was cancelled/removed while a Supabase account (id {SupabaseAuthUserId}) may have been created for it.",
                    operation.Id, operation.InviteId, operation.CompanyId, operation.SupabaseAuthUserId);

                await auditEventPublisher.PublishAsync(
                    new InviteAcceptanceOrphanedByCancellationAuditEvent(
                        operation.CompanyId, operation.InviteId, operation.EmployeeId,
                        operation.SupabaseAuthUserId, now),
                    CancellationToken.None);

                continue;
            }

            if (invite.IsClaimed)
            {
                // A completed AcceptInvite call (this exact retry, or a concurrent one) already
                // resolved this normally — just behind on reflecting it here.
                operation.MarkCompleted(now);
                logger.LogInformation(
                    "InviteAcceptanceReconciliationJob: operation {OperationId} for invite {InviteId} converged to Completed — invite was already claimed.",
                    operation.Id, operation.InviteId);
            }
        }

        await db.SaveChangesAsync();
    }
}
