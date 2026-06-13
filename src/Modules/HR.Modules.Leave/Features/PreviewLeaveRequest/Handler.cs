using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.PreviewLeaveRequest;

internal sealed class PreviewLeaveRequestHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanyLeaveSettingsReader leaveSettingsReader)
{
    public async Task<Result<PreviewLeaveRequestResponse>> HandleAsync(
        PreviewLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.Id == request.LeaveTypeId && lt.CompanyId == request.CompanyId && lt.IsActive,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<PreviewLeaveRequestResponse>(
                Error.NotFound($"Leave type '{request.LeaveTypeId}' was not found."));

        var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);

        List<DateOnly>? publicHolidayDates = null;
        List<ExcludedPublicHolidayItem> excludedHolidays = [];

        if (leaveSettings.ExcludePublicHolidaysFromLeave)
        {
            var holidays = await dbContext.PublicHolidays
                .Where(h => h.CompanyId == request.CompanyId
                         && h.Date >= request.StartDate
                         && h.Date <= request.EndDate)
                .Select(h => new { h.Date, h.Name })
                .ToListAsync(cancellationToken);

            publicHolidayDates = holidays.Select(h => h.Date).ToList();
            excludedHolidays = holidays
                .Where(h => workingPattern.IsWorkingDay(h.Date.DayOfWeek))
                .OrderBy(h => h.Date)
                .Select(h => new ExcludedPublicHolidayItem(h.Date, h.Name))
                .ToList();
        }

        var totalDays = LeaveCalculator.CalculateTotalDays(
            request.StartDate, request.StartPart,
            request.EndDate, request.EndPart,
            workingPattern, publicHolidayDates);

        var conflicts = await dbContext.LeaveRequests
            .Where(r => r.EmployeeId == request.EmployeeId
                     && r.CompanyId == request.CompanyId
                     && r.Status != LeaveRequestStatus.Rejected
                     && r.Status != LeaveRequestStatus.Cancelled
                     && r.StartDate <= request.EndDate
                     && r.EndDate >= request.StartDate)
            .Select(r => new PreviewConflict(r.Id, r.LeaveTypeId, r.StartDate, r.EndDate, r.Status.ToString()))
            .ToListAsync(cancellationToken);

        var policyYear = LeaveYearCalculator.GetPolicyYear(clock.UtcNowOffset(), leaveSettings.LeaveYearStartMonth);

        var balance = await dbContext.LeaveBalances
            .SingleOrDefaultAsync(
                b => b.EmployeeId == request.EmployeeId
                  && b.CompanyId == request.CompanyId
                  && b.LeaveTypeId == request.LeaveTypeId
                  && b.PolicyYear == policyYear,
                cancellationToken);

        decimal? remainingBalance = balance?.RemainingDays;
        var wouldExceedBalance = totalDays > 0 && (remainingBalance is null || remainingBalance < totalDays);

        return Result.Success(new PreviewLeaveRequestResponse(
            totalDays,
            excludedHolidays,
            conflicts,
            remainingBalance,
            wouldExceedBalance));
    }
}
