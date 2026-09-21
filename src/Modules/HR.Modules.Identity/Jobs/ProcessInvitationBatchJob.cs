using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Bulk employee invitations: processes one <see cref="InvitationBatch"/>'s recipients, sequentially
/// (never in parallel — see remarks on respecting the underlying email provider's own rate limits).
/// Resumable: on (re-)entry this loads every recipient still Waiting OR Processing, so a recipient
/// left "Processing" by a crashed prior run (job process killed mid-loop) is picked up again rather
/// than skipped. One recipient's failure never aborts the loop — each is wrapped in its own
/// try/catch, with unexpected exceptions logged and the recipient marked Failed rather than letting
/// the whole job blow up.
/// </summary>
internal sealed class ProcessInvitationBatchJob(
    IdentityDbContext db,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    IEmployeeInviteCandidateReader inviteCandidateReader,
    IInvitationEmailSender invitationEmailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    IAuditEventPublisher auditEventPublisher,
    ILogger<ProcessInvitationBatchJob> logger)
{
    public async Task RunAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await db.InvitationBatches.SingleOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning("ProcessInvitationBatchJob: no invitation batch found for id {BatchId} — skipping.", batchId);
            return;
        }

        var now = clock.UtcNow;
        batch.MarkProcessing(now);
        await db.SaveChangesAsync(cancellationToken);

        // Re-check eligibility once for the whole batch — cheaper than a per-recipient candidate
        // lookup, and candidates don't change mid-loop for any single run.
        var candidates = await inviteCandidateReader.GetCandidatesAsync(batch.CompanyId, cancellationToken);
        var candidatesById = candidates.ToDictionary(c => c.EmployeeId);

        var recipients = await db.InvitationBatchRecipients
            .Where(r => r.BatchId == batchId
                && (r.Status == InvitationBatchRecipient.StatusWaiting
                    || r.Status == InvitationBatchRecipient.StatusProcessing))
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessRecipientAsync(batch, recipient, candidatesById, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "ProcessInvitationBatchJob: unexpected failure processing recipient {EmployeeId} in batch {BatchId} — marking Failed.",
                    recipient.EmployeeId, batchId);

                recipient.MarkFailed("Unexpected error while processing this invitation.");
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var completedAt = clock.UtcNow;
        batch.MarkCompleted(completedAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessRecipientAsync(
        InvitationBatch batch,
        InvitationBatchRecipient recipient,
        Dictionary<Guid, EmployeeInviteCandidate> candidatesById,
        CancellationToken cancellationToken)
    {
        recipient.MarkProcessing();
        await db.SaveChangesAsync(cancellationToken);

        // Second eligibility recheck ("before processing" — the endpoint already performed the
        // first, "before queuing" recheck). Current DB state may have changed since the batch was
        // queued (e.g. the employee accepted a different invite, or an account was created another
        // way in the meantime).
        if (!candidatesById.ContainsKey(recipient.EmployeeId))
        {
            recipient.MarkSkipped("NotEligible", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var hasLinkedUser = await db.Users.AnyAsync(u => u.Id == recipient.EmployeeId, cancellationToken)
            || await db.UserProfiles.AnyAsync(p => p.Id == recipient.EmployeeId, cancellationToken);
        if (hasLinkedUser)
        {
            recipient.MarkSkipped("AlreadyHasAccount", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (recipient.InviteId is null)
        {
            var openInvite = await db.UserInvites
                .Where(i => i.EmployeeId == recipient.EmployeeId && i.ClaimedAt == null && i.CancelledAt == null)
                .FirstOrDefaultAsync(cancellationToken);

            if (openInvite is not null && !openInvite.IsExpired)
            {
                recipient.MarkSkipped("AlreadyInvited", clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        string inviteLink;
        UserInvite invite;

        if (recipient.InviteId is { } existingInviteId)
        {
            // A previous run already created the UserInvite but crashed/failed before the email
            // send was confirmed — reuse it rather than creating a second one for this recipient.
            var existing = await db.UserInvites.SingleOrDefaultAsync(i => i.Id == existingInviteId, cancellationToken);
            if (existing is null)
            {
                recipient.MarkFailed("Previously created invitation could not be found.");
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            invite = existing;
            inviteLink = inviteLinkBuilder.Build(existing.Token);
        }
        else
        {
            var now = clock.UtcNow;
            // Bulk invitations grant only the standard Employee role — an empty RoleIds list falls
            // back to the base Employee role on acceptance (see UserInvite.PendingRoleIds remarks);
            // no administrative roles are ever granted from this job.
            invite = UserInvite.Create(recipient.EmployeeId, batch.CompanyId, recipient.Email, now, roleIds: [], batch.RequestedByUserId);
            db.UserInvites.Add(invite);

            recipient.RecordInviteCreated(invite.Id);
            await db.SaveChangesAsync(cancellationToken);

            inviteLink = inviteLinkBuilder.Build(invite.Token);
        }

        var names = await employeeNameReader.GetNamesAsync(batch.CompanyId, [recipient.EmployeeId], cancellationToken);
        var recipientName = names.TryGetValue(recipient.EmployeeId, out var n) ? n : null;

        var emailSent = await invitationEmailSender.SendAsync(
            toEmail: recipient.Email,
            recipientName: recipientName,
            actionUrl: inviteLink,
            ct: cancellationToken);

        var processedAt = clock.UtcNow;

        if (emailSent)
        {
            recipient.MarkSent(processedAt);
            // Drives CompanyOnboarding's "Invite your team" completion rule the same way the
            // individual invite flow does — see UserInvite.MarkEmailSent remarks.
            invite.MarkEmailSent(processedAt);
            await db.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new UserInvitedAuditEvent(
                    batch.CompanyId,
                    recipient.EmployeeId,
                    invite.Id,
                    recipient.Email,
                    invite.PendingRoleIds,
                    batch.RequestedByUserId,
                    processedAt),
                cancellationToken);
        }
        else
        {
            recipient.MarkFailed("Email delivery failed");
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
