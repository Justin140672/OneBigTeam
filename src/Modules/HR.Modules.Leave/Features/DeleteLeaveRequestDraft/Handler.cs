using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.DeleteLeaveRequestDraft;

// LEAVE-07: a draft never touched LeaveBalance/ToilTransaction/notifications/tasks, so deleting it
// is a plain hard delete - there is nothing to reverse and no audit trail of a "real" leave event
// to preserve (compare CancelLeaveRequestHandler, which reverses balance usage for a real request).
internal sealed class DeleteLeaveRequestDraftHandler(LeaveDbContext dbContext, IClock clock)
{
    public async Task<Result<DeleteLeaveRequestDraftResponse>> HandleAsync(
        DeleteLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, DeleteLeaveRequestDraftResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<DeleteLeaveRequestDraftResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var draft = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (draft is null)
            return Result.Failure<DeleteLeaveRequestDraftResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (draft.Status != LeaveRequestStatus.Draft)
            return Result.Failure<DeleteLeaveRequestDraftResponse>(
                Error.Validation($"Cannot delete a leave request with status '{draft.Status}' as a draft."));

        dbContext.LeaveRequests.Remove(draft);

        var response = new DeleteLeaveRequestDraftResponse(request.LeaveRequestId);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, clock.UtcNowOffset(), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
