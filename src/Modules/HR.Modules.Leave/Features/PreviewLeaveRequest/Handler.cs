using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.PreviewLeaveRequest;

internal sealed class PreviewLeaveRequestHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IPublicHolidayReader publicHolidayReader)
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
            var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
                request.CompanyId, request.StartDate, request.EndDate, cancellationToken);

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

        // Cross-year requests are rejected rather than split/partially deducted across two policy
        // years - callers must submit two separate leave requests, one per policy year. TOIL is
        // exempt: it is not year-bound by design (earned in one year, taken in another). This
        // mirrors SubmitLeaveRequestHandler so preview and submit stay consistent.
        if (leaveType.Behaviour != LeaveTypeBehaviour.Toil)
        {
            var startPolicyYear = LeaveYearCalculator.GetPolicyYear(request.StartDate, leaveSettings.LeaveYearStartMonth);
            var endPolicyYear = LeaveYearCalculator.GetPolicyYear(request.EndDate, leaveSettings.LeaveYearStartMonth);

            if (startPolicyYear != endPolicyYear)
                return Result.Failure<PreviewLeaveRequestResponse>(
                    Error.Validation(
                        "This request spans two leave policy years. Please submit two separate leave requests, one for each policy year."));
        }

        decimal? remainingBalance = null;
        var wouldExceedBalance = false;

        if (leaveType.HasBalance)
        {
            var policyYear = LeaveYearCalculator.GetPolicyYear(request.StartDate, leaveSettings.LeaveYearStartMonth);

            var balance = await dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == request.EmployeeId
                      && b.CompanyId == request.CompanyId
                      && b.LeaveTypeId == request.LeaveTypeId
                      && b.PolicyYear == policyYear,
                    cancellationToken);

            remainingBalance = balance?.RemainingDays;
            wouldExceedBalance = totalDays > 0 && (remainingBalance is null || remainingBalance < totalDays);
        }

        return Result.Success(new PreviewLeaveRequestResponse(
            totalDays,
            excludedHolidays,
            conflicts,
            remainingBalance,
            wouldExceedBalance));
    }
}
