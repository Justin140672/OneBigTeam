using Hangfire;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

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

        if (request.Status == Domain.LeavePolicyDeactivationOnDeparture.StatusProcessed)
            return;

        var now = clock.UtcNowOffset();
        request.MarkProcessing(now);
        await db.SaveChangesAsync();

        try
        {
            var assignment = await db.EmployeeLeavePolicyAssignments
                .FirstOrDefaultAsync(a => a.CompanyId == request.CompanyId && a.EmployeeId == request.EmployeeId);

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

            throw;
        }
    }
}
