using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Authorization;

using AppAuthorizationService = HR.SharedKernel.IAuthorizationService;

internal sealed class PermissionAuthorizationHandler(
    ICurrentUser currentUser,
    AppAuthorizationService authorizationService,
    PermissionDenialAuditThrottle denialThrottle,
    IAuditEventPublisher auditEventPublisher,
    IAdministrativeAlertWriter administrativeAlertWriter,
    ILogger<PermissionAuthorizationHandler> logger,
    IClock clock) : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>
    /// P1 "Login as Customer" support sessions: the fixed, narrow, read-only permission grant a
    /// support session is allowed to satisfy — deliberately NOT resolved through the normal
    /// role/permission-assignment lookup (a support session has no identity.user_profiles row and
    /// must never be given one, or it would become a real employee identity). Chosen conservatively:
    /// only EmployeeRead is a genuinely read-only, unambiguous permission in the current catalogue.
    /// Several other candidates the ticket names ("company settings", "audit logs") are, in this
    /// codebase today, gated behind permissions that are also used to authorize real mutations on
    /// other endpoints (e.g. "company:manage" / EmployeeEdit) — granting those to a support session
    /// would risk silently handing it write access somewhere else, which is a materially worse
    /// outcome than a temporarily narrower support view. Broadening this list is a deliberate,
    /// reviewable follow-up once those permissions are split into distinct read/write ids.
    /// </summary>
    private static readonly IReadOnlySet<Guid> SupportSessionReadOnlyPermissions =
        new HashSet<Guid> { HR.Modules.Identity.Domain.SystemPermissions.EmployeeRead };

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (currentUser.UserId is null)
            return;

        if (currentUser.IsSupportSession)
        {
            // Support sessions are never looked up against identity.user_profiles / role
            // assignments — they simply satisfy (or don't) this fixed allow-list. No denial audit
            // is raised for the "not in the allow-list" case: that is expected, routine behaviour
            // for a deliberately narrow grant, not a security-relevant access attempt to escalate.
            if (SupportSessionReadOnlyPermissions.Contains(requirement.PermissionId))
                context.Succeed(requirement);
            return;
        }

        var hasPermission = await authorizationService.HasPermissionAsync(currentUser.UserId.Value, requirement.PermissionId);
        if (hasPermission)
        {
            context.Succeed(requirement);
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var companyId))
            return;

        if (denialThrottle.ShouldAudit(currentUser.UserId.Value, requirement.PermissionId, out var isEscalation, out var count))
        {
            await auditEventPublisher.PublishAsync(
                new PermissionDeniedAuditEvent(
                    companyId, currentUser.UserId.Value, requirement.PermissionId, count, isEscalation, clock.UtcNowOffset()),
                CancellationToken.None);

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
