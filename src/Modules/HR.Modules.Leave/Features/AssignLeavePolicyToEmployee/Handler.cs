using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed class AssignLeavePolicyToEmployeeHandler(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader)
{
    public async Task<Result<AssignLeavePolicyToEmployeeResponse>> HandleAsync(
        AssignLeavePolicyToEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.Id == request.LeavePolicyId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (policy is null)
        {
            return Result.Failure<AssignLeavePolicyToEmployeeResponse>(
                Error.NotFound($"Leave policy with id '{request.LeavePolicyId}' was not found."));
        }

        var now = clock.UtcNowOffset();

        var existing = await dbContext.EmployeeLeavePolicyAssignments
            .SingleOrDefaultAsync(
                a => a.EmployeeId == request.EmployeeId && a.CompanyId == request.CompanyId,
                cancellationToken);

        bool isNewAssignment = existing is null;
        EmployeeLeavePolicyAssignment assignment;

        if (existing is not null)
        {
            existing.Update(request.LeavePolicyId, request.EffectiveFrom, now);
            assignment = existing;
        }
        else
        {
            assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                request.LeavePolicyId,
                request.EffectiveFrom,
                now);

            dbContext.EmployeeLeavePolicyAssignments.Add(assignment);
        }

        // Initialise leave balances when assigning a policy to an employee for the first time,
        // matching the behaviour of EmployeeCreatedHandler which runs at employee creation.
        if (isNewAssignment)
        {
            // Only balance-tracked leave types get a LeaveBalance row (see LeaveType.HasBalance).
            var activeLeaveTypes = await dbContext.LeaveTypes
                .Where(lt => lt.CompanyId == request.CompanyId && lt.IsActive && lt.HasBalance)
                .ToListAsync(cancellationToken);

            if (activeLeaveTypes.Count > 0)
            {
                var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);
                var policyYear = LeaveYearCalculator.GetPolicyYear(now, leaveSettings.LeaveYearStartMonth);
                var (policyYearStart, _) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);

                // Accrual (Monthly/Fortnightly - LEAVE-04) is paced from the later of the policy
                // year start and the date this assignment takes effect (mirrors
                // EmployeeCreatedHandler's equivalent joiner logic).
                var accrualStartDate = request.EffectiveFrom < policyYearStart ? policyYearStart : request.EffectiveFrom;

                var existingLeaveTypeIds = await dbContext.LeaveBalances
                    .Where(b => b.CompanyId == request.CompanyId
                             && b.EmployeeId == request.EmployeeId
                             && b.PolicyYear == policyYear)
                    .Select(b => b.LeaveTypeId)
                    .ToListAsync(cancellationToken);

                var newBalances = activeLeaveTypes
                    .Where(lt => !existingLeaveTypeIds.Contains(lt.Id))
                    .Select(lt => LeaveBalance.Create(
                        Guid.NewGuid(),
                        request.CompanyId,
                        request.EmployeeId,
                        lt.Id,
                        request.LeavePolicyId,
                        policyYear,
                        lt.Behaviour == LeaveTypeBehaviour.Toil ? 0 : lt.DefaultEntitlementDays,
                        accrualStartDate,
                        now));

                dbContext.LeaveBalances.AddRange(newBalances);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new AssignLeavePolicyToEmployeeResponse(
            assignment.Id,
            assignment.CompanyId,
            assignment.EmployeeId,
            assignment.LeavePolicyId,
            assignment.EffectiveFrom,
            assignment.CreatedAt));
    }
}
