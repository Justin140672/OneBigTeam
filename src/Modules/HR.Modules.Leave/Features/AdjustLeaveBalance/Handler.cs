using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed class AdjustLeaveBalanceHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<AdjustLeaveBalanceResponse>> HandleAsync(
        AdjustLeaveBalanceRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up item 3: the security/ownership boundary this key is scoped to -
        // the client-supplied key alone is never trusted as identity. ActorId is the employee who
        // performed the adjustment (already resolved by the endpoint from the authenticated user).
        var scope = new IdempotencyScope(nameof(AdjustLeaveBalanceHandler), request.CompanyId, request.AdjustedByEmployeeId);

        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the adjustment. Checked ahead of the
        // atomic insert-or-replay in SaveIdempotentAsync below, which also catches a same-key
        // request that races in concurrently.
        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AdjustLeaveBalanceResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AdjustLeaveBalanceResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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
                // Uses accrued (not raw) entitlement so a manual adjustment can't push a
                // Monthly/Fortnightly balance below zero relative to what has actually accrued -
                // consistent with SubmitLeaveRequestHandler's balance-sufficiency check (LEAVE-04).
                var (_, adjustPolicyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);
                var accruedDays = leaveType.Behaviour == LeaveTypeBehaviour.Toil
                    ? balance.EntitlementDays
                    : LeaveAccrualCalculator.CalculateAccruedDays(
                        balance.EntitlementDays,
                        leaveType.AccrualMethod,
                        balance.AccrualStartDate,
                        adjustPolicyYearEnd,
                        DateOnly.FromDateTime(clock.UtcNowOffset().Date));

                var projectedRemaining = accruedDays + balance.AdjustmentDays + adjustmentDays - balance.UsedDays;
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

        var newRemainingHours = balance.RemainingDays * workingPattern.HoursPerDay;

        // Built from in-memory values ahead of the save (balance.Adjust already ran above), so it
        // can double as both the response and the payload persisted for an idempotency replay.
        var response = new AdjustLeaveBalanceResponse(
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
            adjustment.AdjustedAt);

        // Ticket 3 (P1) follow-up item 5: stage the audit intent in the SAME transaction as the
        // business write and the idempotency record, instead of publishing after commit. A crash
        // between "committed" and "published" can now only delay delivery (the background
        // dispatcher retries the outbox row until it succeeds), never lose it. On an idempotent
        // replay (below) this line is never reached, so a replay can never enqueue a second outbox
        // row for the same logical adjustment.
        dbContext.AuditOutboxEntries.EnqueueAuditOutbox(new LeaveBalanceAdjustedAuditEvent(
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
            Reason: request.Reason.ToString()), request.CompanyId, now);

        // Explicit transaction per ticket requirement, even though both writes share one DbContext/SaveChangesAsync.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                // Lost a race against a concurrent duplicate under the same key. SaveIdempotentAsync
                // already rolled back this attempt's transaction (including the staged outbox entry
                // above) - nothing here was committed, so skip our own commit and hand back the
                // winner's result untouched. Its own outbox row continues delivery independently.
                return Result.Success(outcome.Response!);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return Result.Success(response);
    }
}
