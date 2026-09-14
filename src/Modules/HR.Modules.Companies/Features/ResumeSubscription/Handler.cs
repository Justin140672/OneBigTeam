using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.ResumeSubscription;

internal sealed class ResumeSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentTenant currentTenant,
    IClock clock)
{
    public async Task<Result<ResumeSubscriptionResponse>> HandleAsync(
        CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<ResumeSubscriptionResponse>(
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
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ResumeSubscriptionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ResumeSubscriptionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ResumeSubscriptionResponse>(
                Error.NotFound("No subscription record was found for this company."));
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            return Result.Failure<ResumeSubscriptionResponse>(
                Error.Validation("This company has no active Stripe subscription to resume."));
        }

        await stripeGateway.ResumeSubscriptionAsync(subscription.StripeSubscriptionId, cancellationToken);

        subscription.Resume(clock.UtcNowOffset());

        var response = new ResumeSubscriptionResponse(subscription.CancelAtPeriodEnd);

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
