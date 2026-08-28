using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;

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
        }
    }
}
