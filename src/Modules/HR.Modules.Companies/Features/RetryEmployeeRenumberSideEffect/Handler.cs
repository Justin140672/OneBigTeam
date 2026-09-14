using Hangfire;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Jobs;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

/// <summary>
/// SET-08: "a failed renumber operation is visible and can be retried" — resets a Failed outbox
/// row back to Pending and re-enqueues the job. Requires "hr-settings:manage", same as the
/// settings endpoints that can trigger this side effect in the first place.
/// </summary>
internal sealed class RetryEmployeeRenumberSideEffectHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<RetryEmployeeRenumberSideEffectResponse>> HandleAsync(
        RetryEmployeeRenumberSideEffectRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, RetryEmployeeRenumberSideEffectResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RetryEmployeeRenumberSideEffectResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var message = await dbContext.OutboxMessages
            .SingleOrDefaultAsync(
                m => m.Id == request.OutboxMessageId && m.CompanyId == request.CompanyId,
                cancellationToken);

        if (message is null)
            return Result.Failure<RetryEmployeeRenumberSideEffectResponse>(
                Error.NotFound($"Employee renumber side effect '{request.OutboxMessageId}' was not found."));

        if (message.Status != OutboxMessage.StatusFailed)
            return Result.Failure<RetryEmployeeRenumberSideEffectResponse>(
                Error.Validation($"Cannot retry a side effect with status '{message.Status}'. Only a Failed side effect can be retried."));

        var now = clock.UtcNowOffset();
        message.ResetForRetry(now);

        var response = new RetryEmployeeRenumberSideEffectResponse(message.Id, message.CompanyId, message.Status);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                // Lost a race against a concurrent duplicate under the same key - the winner's
                // attempt already enqueued the retry job, so don't enqueue a second one here.
                return Result.Success(outcome.Response!);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        backgroundJobClient.Enqueue<EmployeeRenumberSideEffectJob>(job => job.ProcessAsync(message.Id, message.CompanyId));

        return Result.Success(response);
    }
}
