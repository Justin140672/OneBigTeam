using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.ExecuteCustomerDeletion;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler (see its remarks).
///
/// SCOPE (deliberately conservative, matching Login-As-Customer's precedent): executing a deletion
/// here only performs a safe, reversible-in-principle status transition — it stamps
/// DeletionExecutedAt and forces read-only mode to revoke customer access. It does NOT hard-delete
/// the company's actual data (employees, documents, leave records, etc.) across other modules.
/// Real cross-module data destruction is a materially larger, irreversible, high-blast-radius
/// operation that deserves its own dedicated, carefully-reviewed story — not something bolted onto
/// this admin action. See CustomerSubscription.ExecuteDeletion for the same note at the domain
/// layer.
/// </summary>
internal sealed class ExecuteCustomerDeletionHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IOrganisationDataExportStatusReader exportStatusReader)
{
    public async Task<Result<ExecuteCustomerDeletionResponse>> HandleAsync(
        ExecuteCustomerDeletionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<ExecuteCustomerDeletionResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ExecuteCustomerDeletionResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ExecuteCustomerDeletionResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ExecuteCustomerDeletionResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        // Story 2: do not execute deletion while a full organisation data export is still being
        // prepared for this company — the customer may still need to download it.
        if (await exportStatusReader.HasActiveExportAsync(subscription.CompanyId, cancellationToken))
        {
            return Result.Failure<ExecuteCustomerDeletionResponse>(Error.Conflict(
                "An organisation data export is currently being prepared for this company. Wait for it to finish before executing deletion."));
        }

        var now = clock.UtcNowOffset();

        var executeResult = subscription.ExecuteDeletion(now);
        if (executeResult.IsFailure)
        {
            return Result.Failure<ExecuteCustomerDeletionResponse>(executeResult.Error);
        }

        var response = new ExecuteCustomerDeletionResponse(
            subscription.CompanyId, subscription.DeletionExecutedAt!.Value);

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
            new CustomerDeletionExecutedAuditEvent(
                subscription.CompanyId, currentUser.UserId, now, request.Reason),
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
