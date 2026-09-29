using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

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
                logger.LogError(ex,
                    "Leave year rollover failed for company {CompanyId}, policy year {PolicyYear}",
                    companyId,
                    currentPolicyYear);
            }
        }
    }
}
