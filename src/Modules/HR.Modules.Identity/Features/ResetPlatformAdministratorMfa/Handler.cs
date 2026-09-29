using System.Text.Encodings.Web;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

internal sealed class ResetPlatformAdministratorMfaHandler(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IEmailSender emailSender,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ILogger<ResetPlatformAdministratorMfaHandler> logger)
{
    public async Task<Result<ResetPlatformAdministratorMfaResponse>> HandleAsync(
        ResetPlatformAdministratorMfaRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(Error.NotFound("Platform administrator was not found."));

        if (!administrator.IsEnabled)
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(
                Error.Conflict("Platform administrator account is disabled. Re-enable it before resetting MFA."));

        if (administrator.Role == PlatformAdministratorRole.PlatformOwner
            && !await HasOtherEnabledPlatformOwnerAsync(db, administrator.Id, cancellationToken))
        {
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(
                Error.Conflict("Cannot reset MFA for the last enabled platform owner."));
        }

        var now = clock.UtcNow;

        var supabaseUserId = administrator.SupabaseAuthUserId;
        if (supabaseUserId is null)
        {
            try
            {
                supabaseUserId = await supabaseAuthGateway.GetUserIdByEmailAsync(administrator.Email, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "ResetPlatformAdministratorMfa failed at stage {FailureStage} ({ExceptionType}). AdministratorId={AdministratorId}",
                    "provider_account_lookup", ex.GetType().FullName, administrator.Id);
                supabaseUserId = null;
            }

            if (supabaseUserId is { } resolvedId)
            {
                administrator.LinkSupabaseAuthUserId(resolvedId);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        if (supabaseUserId is not { } targetSupabaseUserId)
        {
            await auditEventPublisher.PublishAsync(
                new PlatformAdministratorMfaResetAuditEvent(
                    administrator.Id, administrator.Email, currentUser.UserId, request.Reason,
                    Succeeded: false, FactorsRemoved: 0, NotificationDelivered: false,
                    FailureReason: "no_linked_identity_provider_account", now),
                cancellationToken);

            return Result.Failure<ResetPlatformAdministratorMfaResponse>(Error.Conflict(
                $"This administrator has no linked identity-provider account (provisioning status: " +
                $"{administrator.ProvisioningStatus}), so MFA cannot be reset. Complete or retry activation first."));
        }

        int factorsRemoved;
        try
        {
            factorsRemoved = await supabaseAuthGateway.RemoveAllMfaFactorsAsync(targetSupabaseUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "MFA reset for platform administrator {AdministratorId} failed at the identity provider.",
                administrator.Id);

            await auditEventPublisher.PublishAsync(
                new PlatformAdministratorMfaResetAuditEvent(
                    administrator.Id, administrator.Email, currentUser.UserId, request.Reason,
                    Succeeded: false, FactorsRemoved: 0, NotificationDelivered: false,
                    FailureReason: "identity_provider_rejected", now),
                cancellationToken);

            return Result.Failure<ResetPlatformAdministratorMfaResponse>(Error.Unexpected(
                "The identity provider rejected the MFA reset. No changes were made. Please retry, and contact support if it keeps failing."));
        }

        var notificationDelivered = await TryNotifyAffectedAdministratorAsync(
            administrator.Id, administrator.Email, now, cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorMfaResetAuditEvent(
                administrator.Id, administrator.Email, currentUser.UserId, request.Reason,
                Succeeded: true, factorsRemoved, notificationDelivered, FailureReason: null, now),
            cancellationToken);

        logger.LogInformation(
            "Reset MFA for platform administrator {AdministratorId}: {FactorsRemoved} factor(s) removed. NotificationDelivered={NotificationDelivered}",
            administrator.Id, factorsRemoved, notificationDelivered);

        return Result.Success(new ResetPlatformAdministratorMfaResponse(
            administrator.Id, administrator.Email, factorsRemoved, notificationDelivered));
    }

    private async Task<bool> TryNotifyAffectedAdministratorAsync(
        Guid administratorId, string email, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        try
        {
            var subject = "Your administrator account MFA has been reset";
            var encodedEmail = HtmlEncoder.Default.Encode(email);
            var body =
                $"<p>The multi-factor authentication (MFA) on your platform administrator account (<strong>{encodedEmail}</strong>) " +
                $"was reset by a platform owner on {occurredAt:u}.</p>" +
                "<p>All previously enrolled MFA factors have been removed. You will be prompted to enrol MFA again the next time you sign in.</p>" +
                "<p>If you did not expect this change, contact the platform owners immediately.</p>";

            await emailSender.SendAsync(email, subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Failed to send MFA-reset notification email ({ExceptionType}). AdministratorId={AdministratorId}",
                ex.GetType().FullName, administratorId);
            return false;
        }
    }

    private static async Task<bool> HasOtherEnabledPlatformOwnerAsync(
        IdentityDbContext db, Guid excludeAdministratorId, CancellationToken cancellationToken) =>
        await db.PlatformAdministrators.AnyAsync(
            a => a.Id != excludeAdministratorId
                 && a.IsEnabled
                 && a.Role == PlatformAdministratorRole.PlatformOwner,
            cancellationToken);
}
