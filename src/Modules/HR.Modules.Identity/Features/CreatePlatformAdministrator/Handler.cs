using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

internal sealed class CreatePlatformAdministratorHandler(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IClock clock,
    IConfiguration configuration,
    IAuditEventPublisher auditEventPublisher,
    AccountCreationEmailGuard accountCreationEmailGuard,
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

        // Ticket 9: new platform administrator accounts must use an organisation email address.
        // Checked before the existing-administrator lookup and before any local row, provider
        // account or onboarding email is created. Existing administrators are unaffected.
        var emailPolicy = await accountCreationEmailGuard.EnsureAllowedAsync(
            request.Email, AccountCreationPath.PlatformAdministrator,
            companyId: Guid.Empty, subjectEmployeeId: null, currentUser.UserId, cancellationToken);
        if (emailPolicy.IsFailure)
            return Result.Failure<CreatePlatformAdministratorResponse>(emailPolicy.Error);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var alreadyExists = await db.PlatformAdministrators
            .AnyAsync(a => a.Email == normalizedEmail, cancellationToken);
        if (alreadyExists)
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Conflict("A platform administrator with this email already exists."));

        var now = clock.UtcNow;
        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);

        var correlationId = Guid.NewGuid();
        Guid? existingProviderUserId;
        try
        {
            existingProviderUserId = await supabaseAuthGateway.GetUserIdByEmailAsync(normalizedEmail, cancellationToken);
        }
        catch (Exception ex)
        {
            // CodeQL #61: operational logs never carry the submitted email address, masked or not.
            // The exception object itself is deliberately NOT logged either: this lookup is keyed
            // by email, so a provider exception message may echo the address back. Only the
            // exception type plus non-personal identifiers are recorded; no administrator record
            // exists yet at this stage, so the correlation id is the durable diagnostic handle.
            logger.LogError(
                "CreatePlatformAdministrator failed at stage {FailureStage} ({ExceptionType}) before any local record was created. CorrelationId={CorrelationId} ActorUserId={ActorUserId}",
                "provider_account_lookup", ex.GetType().FullName, correlationId, currentUser.UserId);
            return Result.Failure<CreatePlatformAdministratorResponse>(Error.Unexpected(
                "Could not reach the identity provider to check for an existing account. No changes were made — please retry."));
        }

        var isNewProviderAccount = existingProviderUserId is null;

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
            return Result.Failure<CreatePlatformAdministratorResponse>(
                Error.Conflict("A platform administrator with this email already exists."));
        }

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorCreatedAuditEvent(
                administrator.Id, administrator.Email, administrator.Role, currentUser.UserId, nowOffset),
            cancellationToken);

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
                await supabaseAuthGateway.RequestPasswordResetAsync(administrator.Email, redirectTo, cancellationToken);
            }

            administrator.MarkProvisioningDelivered(now);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var failureStage = isNewProviderAccount ? "provider_account_creation_failed" : "link_verification_email_failed";

            logger.LogError(
                "Platform administrator provisioning failed at stage {FailureStage} ({ExceptionType}). AdministratorId={AdministratorId} CorrelationId={CorrelationId}",
                failureStage, ex.GetType().FullName, administrator.Id, correlationId);

            administrator.MarkProvisioningFailed(failureStage, now);

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
