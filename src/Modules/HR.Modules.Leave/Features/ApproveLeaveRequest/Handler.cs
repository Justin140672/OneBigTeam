using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestHandler(LeaveDbContext dbContext, IClock clock, LeaveApprovalEffectsService approvalEffects)
{
    public async Task<Result<ApproveLeaveRequestResponse>> HandleAsync(
        ApproveLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ApproveLeaveRequestResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ApproveLeaveRequestResponse>(
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
            return Result.Failure<ApproveLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            return Result.Failure<ApproveLeaveRequestResponse>(
                Error.Validation($"Cannot approve a leave request with status '{leaveRequest.Status}'."));

        var now = clock.UtcNowOffset();

        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(lt => lt.Id == leaveRequest.LeaveTypeId, cancellationToken);

        // TOIL is not year-bound: earned in one year, taken in another, and is tracked as a
        // ledger of individual awards ("buckets") rather than a single balance - see
        // ToilLedgerService for the FIFO consumption/multi-bucket-split algorithm. The mutation
        // and Approve() call both live in LeaveApprovalEffectsService (LEAVE-07) so manual
        // approval and policy-driven automatic approval share identical behaviour.
        var effectResult = await approvalEffects.ApplyBalanceEffectsAndApproveAsync(
            leaveRequest, leaveType, request.ReviewedByEmployeeId, now, cancellationToken);

        if (effectResult.IsFailure)
            return Result.Failure<ApproveLeaveRequestResponse>(effectResult.Error);

        var response = new ApproveLeaveRequestResponse(
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
            leaveRequest.ReviewedByEmployeeId!.Value,
            leaveRequest.ReviewedAt!.Value,
            leaveRequest.UpdatedAt);

        // P1 #4 (optimistic concurrency): LeaveRequest and LeaveBalance both carry a persisted
        // Version concurrency token (see LeaveRequest.Version / LeaveBalance.Version). A concurrent
        // reject/cancel of this same request, or a concurrent approval of a different request that
        // deducts from the same balance row, causes EF's built-in concurrency check to reject this
        // save with DbUpdateConcurrencyException instead of silently losing a balance update.
        // Nothing commits, so no audit/notification/integration event runs below. The caller must
        // reload and retry - business rules (status, remaining balance) are revalidated from
        // scratch on the next attempt rather than blindly retried here.
        try
        {
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
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<ApproveLeaveRequestResponse>(
                Error.Concurrency("This leave request or its leave balance was changed by someone else. Reload and try again."));
        }

        await approvalEffects.PublishApprovalOutcomeAsync(leaveRequest, request.ReviewedByEmployeeId, now, cancellationToken);

        return Result.Success(response);
    }
}
