using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

// P1: creation is a durable provisioning workflow, not a single local insert — see
// PlatformAdministrator's remarks. Only an enabled PlatformOwner may create new platform
// administrator accounts (defense-in-depth handler-level gate — see remarks below).
internal sealed class CreatePlatformAdministratorHandler(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IClock clock,
    IConfiguration configuration,
    IAuditEventPublisher auditEventPublisher,
    ILogger<CreatePlatformAdministratorHandler> logger)
{
    public async Task<Result<CreatePlatformAdministratorResponse>> HandleAsync(
        CreatePlatformAdministratorRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreatePlatformAdministratorResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreatePlatformAdministratorResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        if (!await IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        // 1. Validate and normalize email (FluentValidation already checked format at the endpoint;
        // normalization here is what the DB unique index and every later lookup key off).
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var alreadyExists = await db.PlatformAdministrators
            .AnyAsync(a => a.Email == normalizedEmail, cancellationToken);
        if (alreadyExists)
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Conflict("A platform administrator with this email already exists."));

        var now = clock.UtcNow;
        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);

        // 2. Determine whether a provider (Supabase Auth) account already exists for this email.
        // A failure here leaves NOTHING persisted yet, so the caller can simply retry the whole
        // request — there is no partial/orphaned local state to reconcile from a lookup failure.
        Guid? existingProviderUserId;
        try
        {
            existingProviderUserId = await supabaseAuthGateway.GetUserIdByEmailAsync(normalizedEmail, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Resolving the identity-provider account for a new platform administrator ({Email}) failed before any local record was created.",
                normalizedEmail);
            return Result.Failure<CreatePlatformAdministratorResponse>(Error.Unexpected(
                "Could not reach the identity provider to check for an existing account. No changes were made — please retry."));
        }

        var isNewProviderAccount = existingProviderUserId is null;
        var correlationId = Guid.NewGuid();

        // 3. Persist the local record FIRST, already in a well-defined Pending* provisioning state
        // (never left silently "created but not really usable"). This is the durable checkpoint a
        // retry (RetryPlatformAdministratorProvisioning) can safely resume from without ever
        // creating a second local row or a second provider account for the same email.
        var administrator = PlatformAdministrator.Create(
            normalizedEmail, request.Role, nowOffset, createdByUserId: currentUser.UserId);
        administrator.BeginProvisioning(isNewProviderAccount, correlationId, nowOffset);

        db.PlatformAdministrators.Add(administrator);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two concurrent creation requests for the same normalized email: the AnyAsync
            // pre-check above cannot prevent this race by itself (classic TOCTOU), but the DB's own
            // unique index on Email is the actual guard — exactly one INSERT can ever win. The
            // loser gets a clean conflict instead of a duplicate row or an unhandled 500.
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Conflict("A platform administrator with this email already exists."));
        }

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorCreatedAuditEvent(
                administrator.Id, administrator.Email, administrator.Role, currentUser.UserId, nowOffset),
            cancellationToken);

        // 4. Now that durable local state is committed (a genuine recoverable checkpoint), attempt
        // the identity-provider side. A failure here does NOT roll back the local row — it moves to
        // Failed, remains fully visible/retryable, and is never duplicated by a subsequent retry.
        var webBaseUrl =
            configuration["WebApp:BaseUrl"]?.TrimEnd('/') ??
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        var redirectTo = $"{webBaseUrl}/platform-admin/activate";

        await AttemptProvisioningDeliveryAsync(
            administrator, isNewProviderAccount, correlationId, redirectTo, currentUser.UserId, nowOffset, cancellationToken);

        var response = new CreatePlatformAdministratorResponse(
            administrator.Id, administrator.Email, administrator.Role, administrator.IsEnabled, administrator.CreatedAt,
            administrator.ProvisioningStatus);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, CreatePlatformAdministratorResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, nowOffset, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }

        return Result.Success(response);
    }

    /// <summary>
    /// Shared by CreatePlatformAdministratorHandler and RetryPlatformAdministratorProvisioningHandler:
    /// attempts the identity-provider side of provisioning for an already-persisted Pending*/Failed
    /// row and saves the resulting outcome (Failed, or left Pending* — never advances to Active here;
    /// only ActivatePlatformAdministratorHandler does that, once the recipient has actually proven
    /// control of the account). Sends the onboarding/link-verification email ONLY after the durable
    /// checkpoint (the provider account existing, or — for an already-existing provider account — no
    /// creation being needed at all) has been reached.
    /// </summary>
    internal async Task AttemptProvisioningDeliveryAsync(
        PlatformAdministrator administrator,
        bool isNewProviderAccount,
        Guid correlationId,
        string redirectTo,
        Guid? actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isNewProviderAccount)
            {
                await supabaseAuthGateway.CreatePendingUserWithMetadataAsync(
                    administrator.Email,
                    redirectTo,
                    new Dictionary<string, string> { ["platform_admin_provisioning_id"] = correlationId.ToString() },
                    cancellationToken);
            }
            else
            {
                // An identity-provider account already exists for this email. Never link to it
                // silently — the recipient must authenticate with their EXISTING credentials
                // (ActivatePlatformAdministratorHandler) before this local row is linked. The email
                // sent here simply asks them to sign in and confirm.
                await supabaseAuthGateway.RequestPasswordResetAsync(administrator.Email, redirectTo, cancellationToken);
            }

            // Moves the row back to the correct Pending* status — a no-op on a first-time attempt
            // (already Pending*), but essential for a retry resuming from Failed.
            administrator.MarkProvisioningDelivered(now);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Identity-provider provisioning step failed for platform administrator {AdministratorId}.",
                administrator.Id);

            administrator.MarkProvisioningFailed(
                isNewProviderAccount ? "provider_account_creation_failed" : "link_verification_email_failed", now);

            await db.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new PlatformAdministratorProvisioningFailedAuditEvent(
                    administrator.Id, administrator.Email, administrator.ProvisioningFailureReason!, actorUserId, now),
                cancellationToken);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    internal static async Task<bool> IsEnabledPlatformOwnerAsync(
        IdentityDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Email))
            return false;

        var normalizedEmail = currentUser.Email.Trim().ToLowerInvariant();
        return await db.PlatformAdministrators.AnyAsync(
            a => a.Email == normalizedEmail && a.IsEnabled && a.Role == PlatformAdministratorRole.PlatformOwner,
            cancellationToken);
    }
}
