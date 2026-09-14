using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.AdminCancelSubscription;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler (see its remarks). Calls the
/// same IStripeGateway as the customer-initiated CancelSubscription feature so Stripe's own state
/// stays authoritative — this is a support-initiated equivalent, not a purely local override.
/// </summary>
internal sealed class AdminCancelSubscriptionHandler(
    CompaniesDbContext dbContext,
    IStripeGateway stripeGateway,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AdminCancelSubscriptionResponse>> HandleAsync(
        AdminCancelSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<AdminCancelSubscriptionResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AdminCancelSubscriptionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AdminCancelSubscriptionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<AdminCancelSubscriptionResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();
        var previousState = new AdminCancelSubscriptionAuditSnapshot(subscription.Status.ToString(), subscription.CancelAtPeriodEnd);

        var cancelResult = subscription.AdminCancelAtPeriodEnd(now);
        if (cancelResult.IsFailure)
        {
            return Result.Failure<AdminCancelSubscriptionResponse>(cancelResult.Error);
        }

        if (!string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            await stripeGateway.CancelSubscriptionAsync(
                subscription.StripeSubscriptionId, atPeriodEnd: true, cancellationToken);
        }

        var response = new AdminCancelSubscriptionResponse(subscription.CompanyId, subscription.CancelAtPeriodEnd);

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
            new SubscriptionCancelledByAdminAuditEvent(
                subscription.CompanyId,
                currentUser.UserId,
                now,
                request.Reason,
                previousState,
                new AdminCancelSubscriptionAuditSnapshot(subscription.Status.ToString(), subscription.CancelAtPeriodEnd)),
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
