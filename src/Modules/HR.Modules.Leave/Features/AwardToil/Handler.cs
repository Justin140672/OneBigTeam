using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AwardToil;

internal sealed class AwardToilHandler(LeaveDbContext dbContext, IClock clock, ICompanyLeaveSettingsReader leaveSettingsReader, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<AwardToilResponse>> HandleAsync(
        AwardToilRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AwardToilResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AwardToilResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var toilLeaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.CompanyId == request.CompanyId
                   && lt.Behaviour == LeaveTypeBehaviour.Toil
                   && lt.IsActive,
                cancellationToken);

        if (toilLeaveType is null)
            return Result.Failure<AwardToilResponse>(
                Error.NotFound("No active TOIL leave type is configured for this company."));

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);
        var policyYear = LeaveYearCalculator.GetPolicyYear(request.OccurredOn, leaveSettings.LeaveYearStartMonth);
        var now = clock.UtcNowOffset();

        var balance = await dbContext.LeaveBalances
            .SingleOrDefaultAsync(
                b => b.EmployeeId == request.EmployeeId
                  && b.CompanyId == request.CompanyId
                  && b.LeaveTypeId == toilLeaveType.Id
                  && b.PolicyYear == policyYear,
                cancellationToken);

        if (balance is null)
        {
            var assignment = await dbContext.EmployeeLeavePolicyAssignments
                .FirstOrDefaultAsync(
                    a => a.CompanyId == request.CompanyId && a.EmployeeId == request.EmployeeId,
                    cancellationToken);

            if (assignment is null)
                return Result.Failure<AwardToilResponse>(
                    Error.NotFound($"Employee '{request.EmployeeId}' has no leave policy assignment."));

            // TOIL is exempt from AccrualMethod entirely (it is earned ad hoc via AwardToil, not
            // via a leave type's configured periodic/annual accrual - see LeaveAccrualCalculator
            // remarks and the existing Behaviour == Toil exemptions in Submit/PreviewLeaveRequest).
            // AccrualStartDate is set to today purely to satisfy the column's NOT NULL constraint;
            // it is never read for TOIL balances.
            balance = LeaveBalance.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                toilLeaveType.Id,
                assignment.LeavePolicyId,
                policyYear,
                0m,
                DateOnly.FromDateTime(now.Date),
                now);

            dbContext.LeaveBalances.Add(balance);
        }

        balance.Adjust(request.Days, now);

        var expiresOn = toilLeaveType.ToilExpiryDays.HasValue
            ? request.OccurredOn.AddDays(toilLeaveType.ToilExpiryDays.Value)
            : (DateOnly?)null;

        var transaction = ToilTransaction.CreateEarned(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            balance.Id,
            request.AwardedByEmployeeId,
            request.Days,
            request.OccurredOn,
            expiresOn,
            request.Notes,
            now);

        dbContext.ToilTransactions.Add(transaction);

        var response = new AwardToilResponse(
            transaction.Id,
            transaction.CompanyId,
            transaction.EmployeeId,
            transaction.LeaveBalanceId,
            transaction.ActorEmployeeId,
            transaction.Days,
            balance.RemainingDays,
            transaction.OccurredOn,
            transaction.Notes,
            transaction.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new LeaveBalanceAdjustedAuditEvent(
            balance.CompanyId,
            balance.EmployeeId,
            balance.LeaveTypeId,
            balance.Id,
            balance.PolicyYear,
            request.Days,
            balance.RemainingDays,
            request.AwardedByEmployeeId,
            now), cancellationToken);

        await auditPublisher.PublishAsync(new ToilAwardedAuditEvent(
            transaction.CompanyId,
            transaction.EmployeeId,
            transaction.Id,
            transaction.LeaveBalanceId,
            transaction.ActorEmployeeId,
            transaction.Days,
            transaction.OccurredOn,
            transaction.Notes,
            now), cancellationToken);

        return Result.Success(response);
    }
}
