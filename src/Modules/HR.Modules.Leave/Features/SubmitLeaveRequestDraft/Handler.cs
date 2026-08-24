using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.SubmitLeaveRequestDraft;

// LEAVE-07: submitting a draft is where the full set of blocking checks that
// SubmitLeaveRequestHandler applies to a direct submission is enforced for the first time -
// cross-year rejection, balance sufficiency (mirrors LEAVE-04's shared LeaveAccrualCalculator
// call) and conflict detection. This intentionally duplicates SubmitLeaveRequestHandler's
// validation logic rather than sharing a helper across the two slices, consistent with vertical
// slice independence (03-vertical-slice-architecture.md); the auto-approval balance/TOIL-ledger
// mutation and audit/notification outcome are NOT duplicated - both this handler and
// ApproveLeaveRequestHandler call the same LeaveApprovalEffectsService.
internal sealed class SubmitLeaveRequestDraftHandler(
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
    public async Task<Result<SubmitLeaveRequestDraftResponse>> HandleAsync(
        SubmitLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (draft is null)
            return Result.Failure<SubmitLeaveRequestDraftResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (draft.Status != LeaveRequestStatus.Draft)
            return Result.Failure<SubmitLeaveRequestDraftResponse>(
                Error.Validation($"Cannot submit a leave request with status '{draft.Status}'."));

        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.Id == draft.LeaveTypeId && lt.CompanyId == request.CompanyId && lt.IsActive,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<SubmitLeaveRequestDraftResponse>(
                Error.NotFound($"Leave type '{draft.LeaveTypeId}' was not found."));

        var assignment = await dbContext.EmployeeLeavePolicyAssignments
            .SingleOrDefaultAsync(
                a => a.EmployeeId == request.EmployeeId && a.CompanyId == request.CompanyId,
                cancellationToken);

        if (assignment is null)
            return Result.Failure<SubmitLeaveRequestDraftResponse>(
                Error.Validation("Employee does not have a leave policy assigned."));

        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(p => p.Id == assignment.LeavePolicyId, cancellationToken);

        var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);

        List<DateOnly>? publicHolidayDates = null;
        if (leaveSettings.ExcludePublicHolidaysFromLeave)
        {
            var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
                request.CompanyId, draft.StartDate, draft.EndDate, cancellationToken);
            publicHolidayDates = holidays.Select(h => h.Date).ToList();
        }

        var totalDays = LeaveCalculator.CalculateTotalDays(
            draft.StartDate, draft.StartPart, draft.EndDate, draft.EndPart, workingPattern, publicHolidayDates);

        if (totalDays == 0)
            return Result.Failure<SubmitLeaveRequestDraftResponse>(
                Error.Validation("The requested date range contains no working days."));

        // Mirrors SubmitLeaveRequestHandler's cross-year rule - see its comment.
        if (leaveType.Behaviour != LeaveTypeBehaviour.Toil)
        {
            var startPolicyYear = LeaveYearCalculator.GetPolicyYear(draft.StartDate, leaveSettings.LeaveYearStartMonth);
            var endPolicyYear = LeaveYearCalculator.GetPolicyYear(draft.EndDate, leaveSettings.LeaveYearStartMonth);

            if (startPolicyYear != endPolicyYear)
                return Result.Failure<SubmitLeaveRequestDraftResponse>(
                    Error.Validation(
                        "This request spans two leave policy years. Please submit two separate leave requests, one for each policy year."));
        }

        if (leaveType.HasBalance && policy is not null && !policy.AllowNegativeBalance)
        {
            var policyYear = LeaveYearCalculator.GetPolicyYear(draft.StartDate, leaveSettings.LeaveYearStartMonth);

            var balance = await dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == request.EmployeeId &&
                         b.CompanyId == request.CompanyId &&
                         b.LeaveTypeId == draft.LeaveTypeId &&
                         b.PolicyYear == policyYear,
                    cancellationToken);

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
                        DateOnly.FromDateTime(clock.UtcNowOffset().Date));

                availableDays = accruedDays + balance.AdjustmentDays - balance.UsedDays;
            }

            if (availableDays is null || availableDays < totalDays)
                return Result.Failure<SubmitLeaveRequestDraftResponse>(
                    Error.Validation(
                        $"Insufficient leave balance. Requested {totalDays} day(s) but only {availableDays ?? 0} remaining."));
        }

        var conflicts = await dbContext.LeaveRequests
            .Where(r => r.EmployeeId == request.EmployeeId
                     && r.CompanyId == request.CompanyId
                     && r.Id != draft.Id
                     && r.Status != LeaveRequestStatus.Rejected
                     && r.Status != LeaveRequestStatus.Cancelled
                     && r.Status != LeaveRequestStatus.Draft
                     && r.StartDate <= draft.EndDate
                     && r.EndDate >= draft.StartDate)
            .Select(r => new SubmitDraftConflictWarning(r.Id, r.LeaveTypeId, r.StartDate, r.EndDate, r.Status.ToString()))
            .ToListAsync(cancellationToken);

        // LEAVE-08: mirrors SubmitLeaveRequestHandler/PreviewLeaveRequestHandler so this warning
        // is consistent across preview and both submission paths.
        var excludedHolidays = (await warningCalculator.GetExcludedPublicHolidaysAsync(
                request.CompanyId, draft.StartDate, draft.EndDate, workingPattern,
                leaveSettings.ExcludePublicHolidaysFromLeave, cancellationToken))
            .Select(h => new SubmitDraftExcludedPublicHolidayItem(h.Date, h.Name))
            .ToList();

        var now = clock.UtcNowOffset();

        // Still Draft at this point - UpdateDraftDetails refreshes TotalDays/AssignLeavePolicy
        // resolves the (possibly now-available) policy before the status transition below.
        draft.UpdateDraftDetails(
            draft.LeaveTypeId, draft.StartDate, draft.StartPart, draft.EndDate, draft.EndPart,
            totalDays, draft.Reason, now);
        draft.AssignLeavePolicy(assignment.LeavePolicyId);

        var requiresApproval = policy?.RequiresApproval ?? true;

        if (requiresApproval)
        {
            draft.MarkSubmittedPending(now);
        }
        else
        {
            // Reviewed-by defaults to the requesting employee - there is no separate approver for
            // policies that skip manual review. See LeaveApprovalEffectsService.
            var effectResult = await approvalEffects.ApplyBalanceEffectsAndApproveAsync(
                draft, leaveType, request.EmployeeId, now, cancellationToken);

            if (effectResult.IsFailure)
                return Result.Failure<SubmitLeaveRequestDraftResponse>(effectResult.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new LeaveSubmittedAuditEvent(
            draft.CompanyId,
            draft.EmployeeId,
            draft.Id,
            draft.LeaveTypeId,
            draft.StartDate,
            draft.EndDate,
            draft.TotalDays,
            draft.Reason,
            now), cancellationToken);

        if (requiresApproval)
        {
            await publisher.PublishAsync(new LeaveRequestedIntegrationEvent(
                draft.CompanyId,
                draft.EmployeeId,
                draft.Id,
                draft.LeaveTypeId,
                draft.StartDate,
                draft.EndDate,
                draft.TotalDays,
                now), cancellationToken);
        }
        else
        {
            await approvalEffects.PublishApprovalOutcomeAsync(draft, request.EmployeeId, now, cancellationToken);
        }

        return Result.Success(new SubmitLeaveRequestDraftResponse(
            draft.Id,
            draft.CompanyId,
            draft.EmployeeId,
            draft.LeaveTypeId,
            draft.LeavePolicyId,
            draft.Status.ToString(),
            draft.StartDate,
            draft.StartPart,
            draft.EndDate,
            draft.EndPart,
            draft.TotalDays,
            draft.Reason,
            draft.UpdatedAt,
            conflicts,
            excludedHolidays));
    }
}
