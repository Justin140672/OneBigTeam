using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed class SubmitLeaveRequestHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IWorkingPatternProvider _workingPatternProvider;
    private readonly ICompanyLeaveSettingsReader _leaveSettingsReader;
    private readonly IIntegrationEventPublisher _publisher;

    public SubmitLeaveRequestHandler(
        LeaveDbContext dbContext,
        IClock clock,
        IWorkingPatternProvider workingPatternProvider,
        ICompanyLeaveSettingsReader leaveSettingsReader,
        IIntegrationEventPublisher publisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _workingPatternProvider = workingPatternProvider;
        _leaveSettingsReader = leaveSettingsReader;
        _publisher = publisher;
    }

    public async Task<Result<SubmitLeaveRequestResponse>> HandleAsync(
        SubmitLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var leaveType = await _dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.Id == request.LeaveTypeId && lt.CompanyId == request.CompanyId && lt.IsActive,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<SubmitLeaveRequestResponse>(
                Error.NotFound($"Leave type '{request.LeaveTypeId}' was not found."));

        var assignment = await _dbContext.EmployeeLeavePolicyAssignments
            .SingleOrDefaultAsync(
                a => a.EmployeeId == request.EmployeeId && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (assignment is null)
            return Result.Failure<SubmitLeaveRequestResponse>(
                Error.Validation("Employee does not have a leave policy assigned."));

        var policy = await _dbContext.LeavePolicies
            .SingleOrDefaultAsync(p => p.Id == assignment.LeavePolicyId, cancellationToken);

        var workingPattern = await _workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var leaveSettings = await _leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);

        List<DateOnly>? publicHolidayDates = null;
        if (leaveSettings.ExcludePublicHolidaysFromLeave)
        {
            publicHolidayDates = await _dbContext.PublicHolidays
                .Where(h => h.CompanyId == request.CompanyId && h.Date >= request.StartDate && h.Date <= request.EndDate)
                .Select(h => h.Date)
                .ToListAsync(cancellationToken);
        }

        var totalDays = LeaveCalculator.CalculateTotalDays(request.StartDate, request.StartPart, request.EndDate, request.EndPart, workingPattern, publicHolidayDates);

        if (totalDays == 0)
            return Result.Failure<SubmitLeaveRequestResponse>(
                Error.Validation("The requested date range contains no working days."));

        if (policy is not null && !policy.AllowNegativeBalance)
        {
            var policyYear = LeaveYearCalculator.GetPolicyYear(_clock.UtcNowOffset(), leaveSettings.LeaveYearStartMonth);

            var balance = await _dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == request.EmployeeId &&
                         b.CompanyId == request.CompanyId &&
                         b.LeaveTypeId == request.LeaveTypeId &&
                         b.PolicyYear == policyYear,
                    cancellationToken);

            if (balance is null || balance.RemainingDays < totalDays)
                return Result.Failure<SubmitLeaveRequestResponse>(
                    Error.Validation(
                        $"Insufficient leave balance. Requested {totalDays} day(s) but only {balance?.RemainingDays ?? 0} remaining."));
        }

        var now = _clock.UtcNowOffset();

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.LeaveTypeId,
            assignment.LeavePolicyId,
            request.StartDate,
            request.StartPart,
            request.EndDate,
            request.EndPart,
            totalDays,
            request.Reason,
            now);

        var conflicts = await _dbContext.LeaveRequests
            .Where(r => r.EmployeeId == request.EmployeeId
                     && r.CompanyId == request.CompanyId
                     && r.Status != LeaveRequestStatus.Rejected
                     && r.Status != LeaveRequestStatus.Cancelled
                     && r.StartDate <= request.EndDate
                     && r.EndDate >= request.StartDate)
            .Select(r => new LeaveConflictWarning(r.Id, r.LeaveTypeId, r.StartDate, r.EndDate, r.Status.ToString()))
            .ToListAsync(cancellationToken);

        _dbContext.LeaveRequests.Add(leaveRequest);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _publisher.PublishAsync(new LeaveRequestedIntegrationEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            now), cancellationToken);

        return Result.Success(new SubmitLeaveRequestResponse(
            leaveRequest.Id,
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.LeaveTypeId,
            leaveRequest.LeavePolicyId,
            leaveRequest.Status.ToString(),
            leaveRequest.StartDate,
            leaveRequest.StartPart,
            leaveRequest.EndDate,
            leaveRequest.EndPart,
            leaveRequest.TotalDays,
            leaveRequest.Reason,
            leaveRequest.CreatedAt,
            conflicts));
    }

}
