using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.ReinstateCustomerSubscription;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler (see its remarks). Calls
/// IStripeGateway's resume operation when a live Stripe subscription still exists (CancelAtPeriodEnd
/// pending) — matches ResumeSubscription's precedent. When the subscription has already reached the
/// terminal Canceled status there is no live Stripe subscription left to resume; the domain method
/// (CustomerSubscription.ReinstateCancelledSubscription) documents this as a local support override
/// that must be followed up with manual Stripe reconciliation — flagged as a risk/assumption in this
/// story's report, not silently assumed to be billing-complete.
/// </summary>
internal sealed class ReinstateCustomerSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ReinstateCustomerSubscriptionResponse>> HandleAsync(
        ReinstateCustomerSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<ReinstateCustomerSubscriptionResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ReinstateCustomerSubscriptionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ReinstateCustomerSubscriptionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ReinstateCustomerSubscriptionResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();
        var previousState = new ReinstateSubscriptionAuditSnapshot(subscription.Status.ToString(), subscription.CancelAtPeriodEnd);
        var wasCancelAtPeriodEndPending = subscription.CancelAtPeriodEnd;

        var reinstateResult = subscription.ReinstateCancelledSubscription(now);
        if (reinstateResult.IsFailure)
        {
            return Result.Failure<ReinstateCustomerSubscriptionResponse>(reinstateResult.Error);
        }

        if (wasCancelAtPeriodEndPending && !string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            await stripeGateway.ResumeSubscriptionAsync(subscription.StripeSubscriptionId, cancellationToken);
        }

        var response = new ReinstateCustomerSubscriptionResponse(
            subscription.CompanyId, subscription.Status.ToString(), subscription.CancelAtPeriodEnd);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new SubscriptionReinstatedByAdminAuditEvent(
                subscription.CompanyId,
                currentUser.UserId,
                now,
                request.Reason,
                previousState,
                new ReinstateSubscriptionAuditSnapshot(subscription.Status.ToString(), subscription.CancelAtPeriodEnd)),
            cancellationToken);

        return Result.Success(response);
    }

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
