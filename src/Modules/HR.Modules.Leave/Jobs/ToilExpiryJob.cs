using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Daily job (LEAVE-06) that expires due TOIL for every company with at least one active,
/// expiry-configured TOIL leave type. Evaluated in the company's own time zone, mirroring
/// LeaveYearRolloverJob/ProcessLeavingEmployeesJob. Delegates the actual expiry work to
/// <see cref="ToilExpiryService"/>, which is independently idempotent, so re-running this job (or
/// Hangfire retrying it) for a company that has already been processed today is a safe no-op.
/// </summary>
internal sealed class ToilExpiryJob(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    ToilExpiryService expiryService,
    ILogger<ToilExpiryJob> logger)
{
    public async Task ExecuteAsync()
    {
        var companyIds = await dbContext.LeaveTypes
            .Where(lt => lt.Behaviour == LeaveTypeBehaviour.Toil && lt.IsActive && lt.ToilExpiryDays != null)
            .Select(lt => lt.CompanyId)
            .Distinct()
            .ToListAsync();

        foreach (var companyId in companyIds)
        {
            var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None);
            var today = clock.TodayIn(timeZoneId);

            try
            {
                var result = await expiryService.ExpireCompanyAsync(companyId, today, CancellationToken.None);

                if (result.TransactionsCreated > 0)
                {
                    logger.LogInformation(
                        "TOIL expiry for company {CompanyId}: expired {Count} bucket(s) as of {AsOf}",
                        companyId,
                        result.TransactionsCreated,
                        today);
                }
            }
            catch (Exception ex)
            {
                // Isolate one company's failure from the rest of the batch - ExpireCompanyAsync's
                // own idempotency guard means a company that already completed is a safe no-op on
                // retry, so only the failed company's remaining work is repeated.
                logger.LogError(ex, "TOIL expiry failed for company {CompanyId}", companyId);
            }
        }
    }
}
