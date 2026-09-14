using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed class AssignLeavePolicyToEmployeeHandler(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<AssignLeavePolicyToEmployeeResponse>> HandleAsync(
        AssignLeavePolicyToEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AssignLeavePolicyToEmployeeResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AssignLeavePolicyToEmployeeResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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
        Guid? previousLeavePolicyId = existing?.LeavePolicyId;
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

        var response = new AssignLeavePolicyToEmployeeResponse(
            assignment.Id,
            assignment.CompanyId,
            assignment.EmployeeId,
            assignment.LeavePolicyId,
            assignment.EffectiveFrom,
            assignment.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new LeavePolicyAssignedAuditEvent(
            assignment.CompanyId,
            assignment.EmployeeId,
            assignment.LeavePolicyId,
            previousLeavePolicyId,
            assignment.EffectiveFrom,
            request.ActorEmployeeId,
            now), cancellationToken);

        return Result.Success(response);
    }
}
