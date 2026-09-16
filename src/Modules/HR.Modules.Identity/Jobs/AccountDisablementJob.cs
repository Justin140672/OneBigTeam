using Hangfire;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// P1 fix: performs and confirms the actual <c>ApplicationUser.IsActive</c> disablement requested
/// by Features/OnEmployeeDepartureFinalised — mirrors
/// HR.Modules.Companies.Jobs.EmployeeRenumberSideEffectJob's shape (idempotency-by-row-status,
/// [AutomaticRetry], attempt tracking, final-attempt-marks-Failed-and-rethrows so the existing
/// BackgroundJobAuditFilter records the standard operational-failure audit trail).
///
/// The success audit event (UserAutoDisabledOnDepartureAuditEvent) is only published after
/// SaveChangesAsync has actually persisted IsActive=false — this job never reports disablement
/// optimistically.
///
/// Ticket 19 (P2): claims are guarded by the SAME optimistic-concurrency primitive
/// (<see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/>) regardless of
/// which of the two possible triggers reaches this method:
///  - A LIVE dispatch (<see cref="ProcessAsync(Guid,Guid)"/>, enqueued directly by
///    OnEmployeeDepartureFinalised for a fresh Pending row) claims the row itself here.
///  - A RECONCILIATION re-enqueue (<see cref="ProcessAsync(Guid,Guid,Guid)"/>,
///    AccountDisablementReconciliationJob) has ALREADY atomically claimed the row before enqueuing
///    — this overload only verifies (<see cref="Domain.AccountDisablement.IsClaimedBy"/>) that the
///    claim is still valid (not stolen by a lease-expiry-triggered reclaim in the meantime) before
///    proceeding, rather than claiming a second time.
/// Whichever caller's SaveChangesAsync actually commits the claim first wins the row (advancing
/// Version); every other concurrent claim attempt for the SAME row observes
/// <see cref="DbUpdateConcurrencyException"/> and backs off without throwing, retrying, or
/// touching the account — this is what makes a live dispatch and a reconciliation dispatch for the
/// SAME operation mutually exclusive.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class AccountDisablementJob(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ILogger<AccountDisablementJob> logger)
{
    public const int MaxAttempts = 4;

    /// <summary>Live-dispatch entry point — claims the row itself (see class remarks).</summary>
    public Task ProcessAsync(Guid accountDisablementId, Guid companyId) =>
        ProcessCoreAsync(accountDisablementId, companyId, alreadyClaimedBy: null);

    /// <summary>Reconciliation entry point — <paramref name="claimedBy"/> is the id
    /// AccountDisablementReconciliationJob already atomically claimed this row under; verified
    /// (not re-claimed) before proceeding (see class remarks).</summary>
    public Task ProcessAsync(Guid accountDisablementId, Guid companyId, Guid claimedBy) =>
        ProcessCoreAsync(accountDisablementId, companyId, alreadyClaimedBy: claimedBy);

    private async Task ProcessCoreAsync(Guid accountDisablementId, Guid companyId, Guid? alreadyClaimedBy)
    {
        var request = await db.AccountDisablements.SingleOrDefaultAsync(d => d.Id == accountDisablementId);

        if (request is null)
        {
            logger.LogWarning(
                "AccountDisablementJob: no account disablement record found for id {AccountDisablementId} — skipping.",
                accountDisablementId);
            return;
        }

        if (request.CompanyId != companyId)
        {
            logger.LogError(
                "AccountDisablementJob: company mismatch for account disablement {AccountDisablementId} — job argument {ArgCompanyId} does not match record's company {ActualCompanyId}.",
                accountDisablementId, companyId, request.CompanyId);
            throw new InvalidOperationException(
                $"AccountDisablement {accountDisablementId} does not belong to company {companyId}.");
        }

        // Idempotency guard: already completed (a prior attempt that succeeded but crashed before
        // marking Processed, or a duplicate enqueue) — no-op.
        if (request.Status == Domain.AccountDisablement.StatusProcessed)
            return;

        var now = clock.UtcNow;

        if (alreadyClaimedBy is { } claimId)
        {
            // Ticket 19 (P2): the reconciler already won the claim race for this row atomically —
            // just verify that claim is still live (its lease hasn't since expired and been
            // reclaimed by someone else while this job sat in the Hangfire queue) rather than
            // claiming again.
            if (!request.IsClaimedBy(claimId, now))
            {
                logger.LogInformation(
                    "AccountDisablementJob: reconciler's claim for account disablement {AccountDisablementId} (company {CompanyId}) is no longer valid — another worker has since claimed it. Skipping.",
                    accountDisablementId, companyId);
                return;
            }
        }
        else
        {
            var expectedVersion = request.Version;
            request.Claim(Guid.NewGuid(), now);

            var claimResult = await db.SaveChangesWithConcurrencyAsync(
                request, expectedVersion,
                "This account disablement is already being processed by another worker.",
                CancellationToken.None);

            if (claimResult.IsFailure)
            {
                // Ticket 19 (P2): lost the claim race — a live dispatch and a reconciliation
                // re-enqueue targeted the SAME row and someone else's SaveChangesAsync committed
                // first. Not a failure of THIS attempt: the winner is responsible for the
                // disablement, so back off silently rather than throwing (which would consume a
                // retry attempt and log an operational-failure audit event for work that is, in
                // fact, already correctly in hand).
                logger.LogInformation(
                    "AccountDisablementJob: lost the claim race for account disablement {AccountDisablementId} (company {CompanyId}) — another worker already owns it.",
                    accountDisablementId, companyId);
                return;
            }
        }

        try
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == request.ApplicationUserId);
            // Ticket 1 (P1): real Supabase-backed accounts (AcceptInvite, self-service SignUp) have
            // no ApplicationUser row — fall back to UserProfile so departure disablement actually
            // covers those accounts too, not just legacy ApplicationUser-backed ones.
            var profile = user is null
                ? await db.UserProfiles.SingleOrDefaultAsync(p => p.Id == request.ApplicationUserId)
                : null;

            if (user is null && profile is null)
            {
                // The linked account no longer exists — nothing left to disable; treat as done.
                request.MarkProcessed(clock.UtcNow);
                await db.SaveChangesAsync();
                return;
            }

            if (user is { IsActive: true })
            {
                user.Deactivate(clock.UtcNow);
            }

            if (profile is { IsActive: true })
            {
                profile.Deactivate(clock.UtcNow);
            }

            var processedAt = clock.UtcNow;
            request.MarkProcessed(processedAt);
            await db.SaveChangesAsync();

            await auditEventPublisher.PublishAsync(
                new UserAutoDisabledOnDepartureAuditEvent(
                    request.CompanyId, request.ApplicationUserId, request.EmployeeId, processedAt),
                CancellationToken.None);

            logger.LogInformation(
                "AccountDisablementJob: disabled application user {ApplicationUserId} following departure of employee {EmployeeId} (company {CompanyId}).",
                request.ApplicationUserId, request.EmployeeId, request.CompanyId);
        }
        catch (Exception ex)
        {
            var isFinalAttempt = request.AttemptCount >= MaxAttempts;

            if (isFinalAttempt)
            {
                request.MarkFailed("Account disablement failed.", clock.UtcNow, MaxAttempts);
                await db.SaveChangesAsync();

                await auditEventPublisher.PublishAsync(
                    new UserAccountDisablementFailedAuditEvent(
                        request.CompanyId, request.ApplicationUserId, request.EmployeeId,
                        ex.Message, clock.UtcNow),
                    CancellationToken.None);

                logger.LogError(ex,
                    "AccountDisablementJob: account disablement permanently failed after {Attempts} attempts for application user {ApplicationUserId} (company {CompanyId}).",
                    MaxAttempts, request.ApplicationUserId, request.CompanyId);
            }
            else
            {
                logger.LogWarning(ex,
                    "AccountDisablementJob: attempt {AttemptCount} failed for application user {ApplicationUserId} (company {CompanyId}) — will retry.",
                    request.AttemptCount, request.ApplicationUserId, request.CompanyId);
            }

            // Rethrow while retries remain so Hangfire schedules the next attempt; rethrow on the
            // final attempt too so BackgroundJobAuditFilter records the standard operational-failure
            // audit trail every other job already relies on.
            throw;
        }
    }
}
