using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Authorization;

using AppAuthorizationService = HR.SharedKernel.IAuthorizationService;

/// <summary>
/// IAM-06: resolves <see cref="PermissionRequirement"/> against the caller's effective permission
/// set. This is the mechanism every named capability policy now runs through, replacing the
/// previous pattern of each policy hard-coding its own allowed-role list — see
/// IdentityModule.AddRolePolicies and Authorization/PolicyCatalog.cs.
///
/// IAM-08: also records a denial audit entry on failure (subject to
/// <see cref="PermissionDenialAuditThrottle"/>'s volume control) so repeated/security-relevant
/// access denials are visible to administrators without every routine denial flooding the audit
/// trail. Never logs the request/response payload — only the permission id and denial count.
/// </summary>
internal sealed class PermissionAuthorizationHandler(
    ICurrentUser currentUser,
    AppAuthorizationService authorizationService,
    PermissionDenialAuditThrottle denialThrottle,
    IAuditEventPublisher auditEventPublisher,
    IAdministrativeAlertWriter administrativeAlertWriter,
    ILogger<PermissionAuthorizationHandler> logger,
    IClock clock) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (currentUser.UserId is null)
            return;

        var hasPermission = await authorizationService.HasPermissionAsync(currentUser.UserId.Value, requirement.PermissionId);
        if (hasPermission)
        {
            context.Succeed(requirement);
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var companyId))
            return; // No resolvable tenant — nothing meaningful to attribute the denial to.

        if (denialThrottle.ShouldAudit(currentUser.UserId.Value, requirement.PermissionId, out var isEscalation, out var count))
        {
            await auditEventPublisher.PublishAsync(
                new PermissionDeniedAuditEvent(
                    companyId, currentUser.UserId.Value, requirement.PermissionId, count, isEscalation, clock.UtcNowOffset()),
                CancellationToken.None);

            // ADM-03: a repeated-denial escalation is a security-relevant event — surface it in the
            // administrative alerts inbox (grouped per user). Best-effort: never let alert-raising
            // affect the authorization outcome. Only on the throttle's escalation signal, so routine
            // one-off denials never create alerts.
            if (isEscalation)
            {
                try
                {
                    await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                        companyId,
                        AdministrativeAlertSeverity.Warning,
                        AdministrativeAlertCategory.Security,
                        "Repeated access denials for a user",
                        $"User {currentUser.UserId.Value} has been repeatedly denied access to a protected resource ({count} denials in the current window).",
                        clock.UtcNowOffset(),
                        DedupKey: $"security:repeated-denial:{currentUser.UserId.Value}",
                        AffectedEntityType: "ApplicationUser",
                        AffectedEntityId: currentUser.UserId.Value,
                        RecommendedAction: "Review this user's role assignments and recent activity.",
                        ActionUrl: null),
                        CancellationToken.None);
                }
                catch (Exception alertEx)
                {
                    logger.LogWarning(alertEx,
                        "PermissionAuthorizationHandler: failed to raise administrative alert for repeated access denials by user {UserId}.",
                        currentUser.UserId.Value);
                }
            }
        }
    }
}
