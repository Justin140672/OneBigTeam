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
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class AccountDisablementJob(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ILogger<AccountDisablementJob> logger)
{
    public const int MaxAttempts = 4;

    public async Task ProcessAsync(Guid accountDisablementId, Guid companyId)
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
        request.MarkProcessing(now);
        await db.SaveChangesAsync();

        try
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == request.ApplicationUserId);
            if (user is null)
            {
                // The linked account no longer exists — nothing left to disable; treat as done.
                request.MarkProcessed(clock.UtcNow);
                await db.SaveChangesAsync();
                return;
            }

            if (user.IsActive)
            {
                user.Deactivate(clock.UtcNow);
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
                request.MarkFailed("Account disablement failed.", clock.UtcNow);
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
