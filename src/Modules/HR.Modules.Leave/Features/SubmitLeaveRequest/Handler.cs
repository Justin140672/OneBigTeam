using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed class SubmitLeaveRequestHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IWorkingPatternProvider _workingPatternProvider;
    private readonly ICompanyLeaveSettingsReader _leaveSettingsReader;
    private readonly IPublicHolidayReader _publicHolidayReader;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly LeaveApprovalEffectsService _approvalEffects;
    private readonly LeaveWarningCalculator _warningCalculator;

    public SubmitLeaveRequestHandler(
        LeaveDbContext dbContext,
        IClock clock,
        IWorkingPatternProvider workingPatternProvider,
        ICompanyLeaveSettingsReader leaveSettingsReader,
        IPublicHolidayReader publicHolidayReader,
        IIntegrationEventPublisher publisher,
        IAuditEventPublisher auditPublisher,
        LeaveApprovalEffectsService approvalEffects,
        LeaveWarningCalculator warningCalculator)
    {
        _dbContext = dbContext;
        _clock = clock;
        _workingPatternProvider = workingPatternProvider;
        _leaveSettingsReader = leaveSettingsReader;
        _publicHolidayReader = publicHolidayReader;
        _publisher = publisher;
        _auditPublisher = auditPublisher;
        _approvalEffects = approvalEffects;
        _warningCalculator = warningCalculator;
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
            var holidays = await _publicHolidayReader.GetPublicHolidaysAsync(
                request.CompanyId, request.StartDate, request.EndDate, cancellationToken);
            publicHolidayDates = holidays.Select(h => h.Date).ToList();
        }

        var totalDays = LeaveCalculator.CalculateTotalDays(request.StartDate, request.StartPart, request.EndDate, request.EndPart, workingPattern, publicHolidayDates);

        if (totalDays == 0)
            return Result.Failure<SubmitLeaveRequestResponse>(
                Error.Validation("The requested date range contains no working days."));

        // Cross-year requests are rejected rather than split/partially deducted across two policy
        // years - callers must submit two separate leave requests, one per policy year. TOIL is
        // exempt: it is not year-bound by design (earned in one year, taken in another).
        if (leaveType.Behaviour != LeaveTypeBehaviour.Toil)
        {
            var startPolicyYear = LeaveYearCalculator.GetPolicyYear(request.StartDate, leaveSettings.LeaveYearStartMonth);
            var endPolicyYear = LeaveYearCalculator.GetPolicyYear(request.EndDate, leaveSettings.LeaveYearStartMonth);

            if (startPolicyYear != endPolicyYear)
                return Result.Failure<SubmitLeaveRequestResponse>(
                    Error.Validation(
                        "This request spans two leave policy years. Please submit two separate leave requests, one for each policy year."));
        }

        if (leaveType.HasBalance && policy is not null && !policy.AllowNegativeBalance)
        {
            var policyYear = LeaveYearCalculator.GetPolicyYear(request.StartDate, leaveSettings.LeaveYearStartMonth);

            var balance = await _dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == request.EmployeeId &&
                         b.CompanyId == request.CompanyId &&
                         b.LeaveTypeId == request.LeaveTypeId &&
                         b.PolicyYear == policyYear,
                    cancellationToken);

            // Uses the same LeaveAccrualCalculator as balance display (GetEmployeeLeaveBalanceHandler)
            // and preview (PreviewLeaveRequestHandler) so the figure enforced here can never diverge
            // from what the employee was shown before submitting (LEAVE-04).
            decimal? availableDays = null;
            if (balance is not null)
            {
                var (_, balancePolicyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);
                var accruedDays = leaveType.Behaviour == LeaveTypeBehaviour.Toil
                    ? balance.EntitlementDays
                    : LeaveAccrualCalculator.CalculateAccruedDays(
                        balance.EntitlementDays,
                        leaveType.AccrualMethod,
                        balance.AccrualStartDate,
                        balancePolicyYearEnd,
                        DateOnly.FromDateTime(_clock.UtcNowOffset().Date));

                availableDays = accruedDays + balance.AdjustmentDays - balance.UsedDays;
            }

            if (availableDays is null || availableDays < totalDays)
                return Result.Failure<SubmitLeaveRequestResponse>(
                    Error.Validation(
                        $"Insufficient leave balance. Requested {totalDays} day(s) but only {availableDays ?? 0} remaining."));
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

        // LEAVE-08: surfaces the same public-holiday-in-range warning PreviewLeaveRequestHandler
        // returns, so a client that skipped preview still sees it on submission.
        var excludedHolidays = (await _warningCalculator.GetExcludedPublicHolidaysAsync(
                request.CompanyId, request.StartDate, request.EndDate, workingPattern,
                leaveSettings.ExcludePublicHolidaysFromLeave, cancellationToken))
            .Select(h => new SubmitExcludedPublicHolidayItem(h.Date, h.Name))
            .ToList();

        _dbContext.LeaveRequests.Add(leaveRequest);

        // LEAVE-07: RequiresApproval lives on the policy, defaulting to true (the safer choice)
        // when the employee has no resolvable policy - see LeavePolicy.RequiresApproval.
        var requiresApproval = policy?.RequiresApproval ?? true;

        if (!requiresApproval)
        {
            // Auto-approval path: apply the exact same balance/TOIL-ledger mutation and Approve()
            // call a manual reviewer's approval would trigger (LeaveApprovalEffectsService),
            // reviewed-by defaults to the requesting employee since there is no separate approver
            // for policies that skip manual review. No LeaveRequestedIntegrationEvent is
            // published, so the Tasks module never creates an approval task for this request.
            var effectResult = await _approvalEffects.ApplyBalanceEffectsAndApproveAsync(
                leaveRequest, leaveType, request.EmployeeId, now, cancellationToken);

            if (effectResult.IsFailure)
                return Result.Failure<SubmitLeaveRequestResponse>(effectResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(new LeaveSubmittedAuditEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            leaveRequest.Reason,
            now), cancellationToken);

        if (requiresApproval)
        {
            await _publisher.PublishAsync(new LeaveRequestedIntegrationEvent(
                leaveRequest.CompanyId,
                leaveRequest.EmployeeId,
                leaveRequest.Id,
                leaveRequest.LeaveTypeId,
                leaveRequest.StartDate,
                leaveRequest.EndDate,
                leaveRequest.TotalDays,
                now), cancellationToken);
        }
        else
        {
            // Produces the same audit/notification outcome a manual approval would (LEAVE-07 AC).
            await _approvalEffects.PublishApprovalOutcomeAsync(leaveRequest, request.EmployeeId, now, cancellationToken);
        }

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
            conflicts,
            excludedHolidays));
    }

}
