using Hangfire;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Performs and confirms the actual <c>EmployeeLeavePolicyAssignment</c> deactivation requested by
/// Features/DeactivateLeavePolicyAssignmentOnEmployeeDeparture — mirrors
/// HR.Modules.Identity.Jobs.AccountDisablementJob's shape (idempotency-by-row-status,
/// [AutomaticRetry], attempt tracking, final-attempt-marks-Failed-and-rethrows).
///
/// Uses the request's captured OccurredAt (the original departure-finalisation timestamp) as the
/// deactivation timestamp on every attempt, including retries/replays, so a retried deactivation
/// never shifts the audit trail to a later "now".
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class LeavePolicyDeactivationJob(
    LeaveDbContext db,
    IClock clock,
    ILogger<LeavePolicyDeactivationJob> logger)
{
    public const int MaxAttempts = 4;

    public async Task ProcessAsync(Guid deactivationId, Guid companyId)
    {
        var request = await db.LeavePolicyDeactivationsOnDeparture.SingleOrDefaultAsync(d => d.Id == deactivationId);

        if (request is null)
        {
            logger.LogWarning(
                "LeavePolicyDeactivationJob: no deactivation record found for id {DeactivationId} — skipping.",
                deactivationId);
            return;
        }

        if (request.CompanyId != companyId)
        {
            logger.LogError(
                "LeavePolicyDeactivationJob: company mismatch for deactivation {DeactivationId} — job argument {ArgCompanyId} does not match record's company {ActualCompanyId}.",
                deactivationId, companyId, request.CompanyId);
            throw new InvalidOperationException(
                $"LeavePolicyDeactivationOnDeparture {deactivationId} does not belong to company {companyId}.");
        }

        // Idempotency guard: already completed (a prior attempt that succeeded but crashed before
        // marking Processed, or a duplicate enqueue) — no-op.
        if (request.Status == Domain.LeavePolicyDeactivationOnDeparture.StatusProcessed)
            return;

        var now = clock.UtcNowOffset();
        request.MarkProcessing(now);
        await db.SaveChangesAsync();

        try
        {
            var assignment = await db.EmployeeLeavePolicyAssignments
                .FirstOrDefaultAsync(a => a.CompanyId == request.CompanyId && a.EmployeeId == request.EmployeeId);

            // Deactivate() is itself a no-op if already inactive — safe on a retry/replay racing
            // against a manual deactivation or a duplicate job enqueue.
            assignment?.Deactivate(request.OccurredAt);

            var processedAt = clock.UtcNowOffset();
            request.MarkProcessed(processedAt);
            await db.SaveChangesAsync();

            logger.LogInformation(
                "LeavePolicyDeactivationJob: deactivated leave policy assignment for employee {EmployeeId} following departure (company {CompanyId}).",
                request.EmployeeId, request.CompanyId);
        }
        catch (Exception ex)
        {
            var isFinalAttempt = request.AttemptCount >= MaxAttempts;

            if (isFinalAttempt)
            {
                request.MarkFailed("Leave policy deactivation failed.", clock.UtcNowOffset());
                await db.SaveChangesAsync();

                logger.LogError(ex,
                    "LeavePolicyDeactivationJob: deactivation permanently failed after {Attempts} attempts for employee {EmployeeId} (company {CompanyId}).",
                    MaxAttempts, request.EmployeeId, request.CompanyId);
            }
            else
            {
                logger.LogWarning(ex,
                    "LeavePolicyDeactivationJob: attempt {AttemptCount} failed for employee {EmployeeId} (company {CompanyId}) — will retry.",
                    request.AttemptCount, request.EmployeeId, request.CompanyId);
            }

            // Rethrow while retries remain so Hangfire schedules the next attempt; rethrow on the
            // final attempt too so the standard operational-failure audit trail records it.
            throw;
        }
    }
}
