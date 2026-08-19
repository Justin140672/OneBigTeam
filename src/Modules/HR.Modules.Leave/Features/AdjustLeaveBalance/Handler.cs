using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed class AdjustLeaveBalanceHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IEmployeeNameReader employeeNameReader,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<AdjustLeaveBalanceResponse>> HandleAsync(
        AdjustLeaveBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.Id == request.LeaveTypeId && lt.CompanyId == request.CompanyId && lt.IsActive,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<AdjustLeaveBalanceResponse>(
                Error.NotFound($"Leave type '{request.LeaveTypeId}' was not found."));

        // Cross-module employee-existence check via the existing reader abstraction
        // (IEmployeeNameReader is implemented in HR.Modules.Employees and DI-registered
        // against HR.Infrastructure.Abstractions; other modules — e.g. Sickness — already
        // consume it the same way). IDs not found are simply absent from the returned map.
        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [request.EmployeeId], cancellationToken);
        if (!names.ContainsKey(request.EmployeeId))
            return Result.Failure<AdjustLeaveBalanceResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);
        var policyYear = LeaveYearCalculator.GetPolicyYear(clock.UtcNowOffset(), leaveSettings.LeaveYearStartMonth);

        var balance = await dbContext.LeaveBalances
            .SingleOrDefaultAsync(
                b => b.CompanyId == request.CompanyId
                  && b.EmployeeId == request.EmployeeId
                  && b.LeaveTypeId == request.LeaveTypeId
                  && b.PolicyYear == policyYear,
                cancellationToken);

        if (balance is null)
            return Result.Failure<AdjustLeaveBalanceResponse>(
                Error.NotFound($"No leave balance exists for employee '{request.EmployeeId}' and leave type '{request.LeaveTypeId}' in policy year {policyYear}."));

        var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var isToil = leaveType.Behaviour == LeaveTypeBehaviour.Toil;
        var adjustmentDays = isToil ? request.AdjustmentValue / workingPattern.HoursPerDay : request.AdjustmentValue;
        var adjustmentHoursForRecord = isToil ? (decimal?)request.AdjustmentValue : null;

        if (adjustmentDays < 0 && !request.AllowNegativeOverride)
        {
            var policy = await dbContext.LeavePolicies
                .SingleOrDefaultAsync(p => p.Id == balance.LeavePolicyId, cancellationToken);

            var allowNegative = policy?.AllowNegativeBalance ?? false;

            if (!allowNegative)
            {
                var projectedRemaining = balance.EntitlementDays + balance.AdjustmentDays + adjustmentDays - balance.UsedDays;
                if (projectedRemaining < 0)
                    return Result.Failure<AdjustLeaveBalanceResponse>(
                        Error.Validation("This adjustment would take the balance below zero. Enable the override to allow it."));
            }
        }

        var now = clock.UtcNowOffset();

        balance.Adjust(adjustmentDays, now);

        var adjustment = LeaveBalanceAdjustment.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.LeaveTypeId,
            adjustmentDays,
            adjustmentHoursForRecord,
            request.Reason,
            request.Comments,
            request.AdjustedByEmployeeId,
            now);

        dbContext.LeaveBalanceAdjustments.Add(adjustment);

        // Explicit transaction per ticket requirement, even though both writes share one DbContext/SaveChangesAsync.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var newRemainingHours = balance.RemainingDays * workingPattern.HoursPerDay;

        await auditPublisher.PublishAsync(new LeaveBalanceAdjustedAuditEvent(
            balance.CompanyId,
            balance.EmployeeId,
            balance.LeaveTypeId,
            balance.Id,
            balance.PolicyYear,
            adjustmentDays,
            balance.RemainingDays,
            request.AdjustedByEmployeeId,
            now,
            AdjustmentHours: adjustmentHoursForRecord,
            Reason: request.Reason.ToString()), cancellationToken);

        return Result.Success(new AdjustLeaveBalanceResponse(
            adjustment.Id,
            adjustment.CompanyId,
            adjustment.EmployeeId,
            adjustment.LeaveTypeId,
            balance.Id,
            adjustment.AdjustmentDays,
            adjustment.AdjustmentHours,
            balance.RemainingDays,
            newRemainingHours,
            adjustment.Reason.ToString(),
            adjustment.Comments,
            adjustment.AdjustedByEmployeeId,
            adjustment.AdjustedAt));
    }
}
