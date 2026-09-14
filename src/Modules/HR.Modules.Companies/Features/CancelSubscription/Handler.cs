using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.CancelSubscription;

internal sealed class CancelSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<CancelSubscriptionResponse>> HandleAsync(
        CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<CancelSubscriptionResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        // No client-supplied request body to fingerprint (EndpointWithoutRequest) - the resolved
        // company id is the only thing that varies between calls, so it stands in for the
        // fingerprint here.
        var scope = new IdempotencyScope(GetType().Name, companyId, Guid.Empty);

        var fingerprint = idempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(companyId)
            : null;

        if (idempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CancelSubscriptionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CancelSubscriptionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<CancelSubscriptionResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            return Result.Failure<CancelSubscriptionResponse>(
                Error.Validation("This company has no active Stripe subscription to cancel."));
        }

        await stripeGateway.CancelSubscriptionAsync(
            subscription.StripeSubscriptionId, atPeriodEnd: true, cancellationToken);

        subscription.RequestCancellation(clock.UtcNowOffset());

        var response = new CancelSubscriptionResponse(subscription.CancelAtPeriodEnd);

        if (idempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response,
                clock.UtcNowOffset(), cancellationToken);

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
