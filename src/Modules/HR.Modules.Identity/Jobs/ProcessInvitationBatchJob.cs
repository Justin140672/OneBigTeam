using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

internal sealed class ProcessInvitationBatchJob(
    IdentityDbContext db,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    IEmployeeInviteCandidateReader inviteCandidateReader,
    IInvitationEmailSender invitationEmailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    IAuditEventPublisher auditEventPublisher,
    AccountCreationEmailGuard accountCreationEmailGuard,
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

        if (!candidatesById.ContainsKey(recipient.EmployeeId))
        {
            recipient.MarkSkipped("NotEligible", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var hasLinkedUser = await db.UserProfiles.AnyAsync(p => p.Id == recipient.EmployeeId, cancellationToken);
        if (hasLinkedUser)
        {
            recipient.MarkSkipped("AlreadyHasAccount", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Ticket 9: re-check the email-domain policy before creating/sending anything — the queue
        // handler already excluded public/disposable addresses, but the denylist may have been
        // extended between queuing and processing (or a recipient row predates the policy).
        var emailEvaluation = accountCreationEmailGuard.Evaluate(recipient.Email);
        if (!emailEvaluation.IsAllowed)
        {
            recipient.MarkSkipped(AccountCreationEmailGuard.BulkExclusionReason, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            await accountCreationEmailGuard.RecordRejectionsAsync(
                AccountCreationPath.BulkEmployeeInvitation,
                batch.CompanyId,
                [(recipient.EmployeeId, emailEvaluation)],
                batch.RequestedByUserId,
                cancellationToken,
                AuditActorType.ScheduledJob);
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
