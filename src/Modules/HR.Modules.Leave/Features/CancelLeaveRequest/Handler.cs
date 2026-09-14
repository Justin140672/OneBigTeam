using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.CancelLeaveRequest;

internal sealed class CancelLeaveRequestHandler(LeaveDbContext dbContext, IClock clock, ICompanyLeaveSettingsReader leaveSettingsReader, IAuditEventPublisher auditPublisher, ToilLedgerService toilLedgerService)
{
    public async Task<Result<CancelLeaveRequestResponse>> HandleAsync(
        CancelLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CancelLeaveRequestResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CancelLeaveRequestResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var leaveRequest = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (leaveRequest is null)
            return Result.Failure<CancelLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (leaveRequest.Status is LeaveRequestStatus.Cancelled or LeaveRequestStatus.Rejected)
            return Result.Failure<CancelLeaveRequestResponse>(
                Error.Validation($"Cannot cancel a leave request with status '{leaveRequest.Status}'."));

        // LEAVE-07: a Draft was never submitted, so "cancel" is not a meaningful action - use
        // DeleteLeaveRequestDraft instead.
        if (leaveRequest.Status is LeaveRequestStatus.Draft)
            return Result.Failure<CancelLeaveRequestResponse>(
                Error.Validation("Cannot cancel a draft leave request - delete the draft instead."));

        var now = clock.UtcNowOffset();
        var previousStatus = leaveRequest.Status.ToString();

        if (leaveRequest.Status == LeaveRequestStatus.Approved)
        {
            var leaveType = await dbContext.LeaveTypes
                .SingleOrDefaultAsync(lt => lt.Id == leaveRequest.LeaveTypeId, cancellationToken);

            if (leaveType?.Behaviour == LeaveTypeBehaviour.Toil)
            {
                // Reverses every Used ledger transaction recorded against this leave request,
                // restoring each specific bucket it drew from - not a lump sum credit - so future
                // FIFO consumption ordering stays correct. See ToilLedgerService.ReverseAsync.
                await toilLedgerService.ReverseAsync(
                    leaveRequest.CompanyId,
                    leaveRequest.EmployeeId,
                    leaveRequest.Id,
                    leaveRequest.EmployeeId,
                    DateOnly.FromDateTime(now.Date),
                    now,
                    cancellationToken);
            }
            else
            {
                var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(leaveRequest.CompanyId, cancellationToken);
                var policyYear = LeaveYearCalculator.GetPolicyYear(leaveRequest.StartDate, leaveSettings.LeaveYearStartMonth);

                var balance = await dbContext.LeaveBalances
                    .SingleOrDefaultAsync(
                        b => b.EmployeeId == leaveRequest.EmployeeId
                          && b.CompanyId == leaveRequest.CompanyId
                          && b.LeaveTypeId == leaveRequest.LeaveTypeId
                          && b.PolicyYear == policyYear,
                        cancellationToken);

                balance?.ReverseUsage(leaveRequest.TotalDays, now);
            }
        }

        leaveRequest.Cancel(now);

        var response = new CancelLeaveRequestResponse(
            leaveRequest.Id,
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.StartPart,
            leaveRequest.EndDate,
            leaveRequest.EndPart,
            leaveRequest.TotalDays,
            leaveRequest.Status.ToString(),
            leaveRequest.UpdatedAt);

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

        await auditPublisher.PublishAsync(new LeaveCancelledAuditEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            previousStatus,
            now), cancellationToken);

        return Result.Success(response);
    }
}
