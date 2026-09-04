using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

// ADM-06: performs a real MFA reset against the configured identity provider (Supabase) by
// unenrolling every MFA factor for the target administrator's linked Supabase Auth user. Restricted
// to an enabled platform owner; refuses to strand the platform's last enabled owner; requires an
// explicit confirmation + administrative reason; audits every attempt (success and failure); and
// notifies the affected administrator by email (the approved secure channel — see the notify call
// below for the rationale). Returns success only once Supabase has accepted the reset.
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

        // Last-owner safeguard: never allow the platform's final enabled owner to have their MFA
        // cleared out — that would leave no fully-protected owner able to recover the platform.
        if (administrator.Role == PlatformAdministratorRole.PlatformOwner
            && !await HasOtherEnabledPlatformOwnerAsync(db, administrator.Id, cancellationToken))
        {
            return Result.Failure<ResetPlatformAdministratorMfaResponse>(
                Error.Conflict("Cannot reset MFA for the last enabled platform owner."));
        }

        var now = clock.UtcNow;

        // The local row is only linked to a Supabase Auth user id when the administrator has
        // authenticated at least once (see GetPlatformAdminMe) — an admin created through the Admin
        // Portal has none yet. Resolve it by email from the identity provider and back-link it so
        // later operations don't have to.
        var supabaseUserId = administrator.SupabaseAuthUserId;
        if (supabaseUserId is null)
        {
            try
            {
                supabaseUserId = await supabaseAuthGateway.GetUserIdByEmailAsync(administrator.Email, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Resolving the identity-provider account for platform administrator {AdministratorId} failed.",
                    administrator.Id);
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
                "This administrator has no linked identity-provider account, so MFA cannot be reset."));
        }

        int factorsRemoved;
        try
        {
            factorsRemoved = await supabaseAuthGateway.RemoveAllMfaFactorsAsync(targetSupabaseUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The gateway message carries the provider HTTP status/body (never API keys) — log it in
            // full for diagnostics, but return only a generic actionable message to the caller.
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

        // Provider has accepted the reset — the action has succeeded from here on. Notification is
        // best-effort: the affected administrator's MFA is already gone, and per the acceptance
        // criteria success is returned once the provider accepts. A failed email is logged and
        // recorded in the audit outcome rather than rolling back a completed security action.
        var notificationDelivered = await TryNotifyAffectedAdministratorAsync(administrator.Email, now, cancellationToken);

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
        string email, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        // Approved secure channel: a direct email to the administrator's registered address. The
        // administrative alerts inbox (IAdministrativeAlertWriter) is a platform-owner ops queue and
        // is company-scoped; it does not reach the affected person — who, by the nature of an MFA
        // reset, may currently be unable to sign in at all. Email is the same channel already used
        // for every other account-security event (invitations, password resets).
        try
        {
            var subject = "Your administrator account MFA has been reset";
            var body =
                $"<p>The multi-factor authentication (MFA) on your platform administrator account (<strong>{email}</strong>) " +
                $"was reset by a platform owner on {occurredAt:u}.</p>" +
                "<p>All previously enrolled MFA factors have been removed. You will be prompted to enrol MFA again the next time you sign in.</p>" +
                "<p>If you did not expect this change, contact the platform owners immediately.</p>";

            await emailSender.SendAsync(email, subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send MFA-reset notification email to platform administrator {Email}.", email);
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
