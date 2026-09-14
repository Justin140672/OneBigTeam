using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.SetDefaultLeavePolicy;

internal sealed class SetDefaultLeavePolicyHandler(LeaveDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        SetDefaultLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, object?>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.Id == request.Id && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (policy is null)
            return Result.Failure(Error.NotFound($"Leave policy '{request.Id}' was not found."));

        if (!policy.IsActive)
            return Result.Failure(Error.Validation("Cannot set an inactive leave policy as the default."));

        if (policy.IsDefault)
            return Result.Success();

        var now = clock.UtcNowOffset();

        var currentDefault = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.IsDefault,
                cancellationToken);

        // Two separate SaveChanges calls, not one batch: the partial unique index on
        // (company_id) WHERE is_default only defers/checks per-statement, not per-transaction, so
        // if both the unmark and the mark went through in the same batch there'd be a moment where
        // EF could send "mark new default" before "unmark old default" has committed, transiently
        // violating uniqueness even though the end state is valid.
        if (currentDefault is not null)
        {
            currentDefault.UnmarkAsDefault(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        policy.MarkAsDefault(now);

        if (request.IdempotencyKey is { } key)
        {
            // Third SaveChanges call: the two-phase unmark/mark save above must already have
            // committed for the uniqueness-index reason documented above, so the idempotency
            // record is claimed in its own final save rather than batched with either of them.
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, object?>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status204NoContent, null, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
