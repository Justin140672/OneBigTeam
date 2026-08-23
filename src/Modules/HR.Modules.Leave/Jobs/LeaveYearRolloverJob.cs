using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Daily job (LEAVE-03) that triggers the leave-year rollover for each company on the day its
/// configured leave year starts (per <see cref="CompanyLeaveSettings.LeaveYearStartMonth"/>,
/// evaluated in the company's own time zone — mirrors ProcessLeavingEmployeesJob/
/// GenerateDueProbationReviewsJob). Scans every company that has at least one leave policy
/// assignment; a company whose rollover day has not arrived today is skipped entirely (cheap
/// no-op), so running this daily is safe for calendar-year and non-January leave years alike.
///
/// The actual balance/carry-over work is delegated to <see cref="LeaveYearRolloverService"/>,
/// which is independently idempotent — so a company that already completed its rollover today (or
/// on a previous run) is a safe no-op here too.
/// </summary>
internal sealed class LeaveYearRolloverJob(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    ICompanyTimeZoneReader companyTimeZoneReader,
    LeaveYearRolloverService rolloverService,
    ILogger<LeaveYearRolloverJob> logger)
{
    public async Task ExecuteAsync()
    {
        var companyIds = await dbContext.EmployeeLeavePolicyAssignments
            .Select(a => a.CompanyId)
            .Distinct()
            .ToListAsync();

        foreach (var companyId in companyIds)
        {
            var settings = await leaveSettingsReader.GetLeaveSettingsAsync(companyId, CancellationToken.None);
            var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None);
            var today = clock.TodayIn(timeZoneId);

            var currentPolicyYear = LeaveYearCalculator.GetPolicyYear(today, settings.LeaveYearStartMonth);
            var (policyYearStart, _) = LeaveYearCalculator.GetPolicyYearBounds(currentPolicyYear, settings.LeaveYearStartMonth);

            if (today != policyYearStart)
                continue;

            try
            {
                var result = await rolloverService.RolloverCompanyAsync(companyId, currentPolicyYear, CancellationToken.None);

                if (result.BalancesCreated > 0)
                {
                    logger.LogInformation(
                        "Leave year rollover for company {CompanyId}: created {BalanceCount} balance(s) " +
                        "and {CarryOverCount} carry-over adjustment(s) for policy year {PolicyYear}",
                        companyId,
                        result.BalancesCreated,
                        result.CarryOverAdjustmentsCreated,
                        currentPolicyYear);
                }
            }
            catch (Exception ex)
            {
                // Isolate one company's failure from the rest of the batch. Hangfire retries the
                // whole job automatically per its default retry policy, and RolloverCompanyAsync's
                // own idempotency guard means any company that already completed is a safe no-op
                // on retry — only the failed company's remaining work is repeated.
                logger.LogError(ex,
                    "Leave year rollover failed for company {CompanyId}, policy year {PolicyYear}",
                    companyId,
                    currentPolicyYear);
            }
        }
    }
}
