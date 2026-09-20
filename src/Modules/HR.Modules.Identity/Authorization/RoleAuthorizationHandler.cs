using HR.Modules.Identity.Domain;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Authorization;

using AppAuthorizationService = HR.SharedKernel.IAuthorizationService;

internal sealed class RoleAuthorizationHandler(
    ICurrentUser currentUser,
    AppAuthorizationService authorizationService) : AuthorizationHandler<RoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        if (currentUser.UserId is null)
            return;

        // P1 "Login as Customer": a support session satisfies only the "role:employee" floor
        // (used across the app as a generic "is this an authenticated, in-app session" gate, e.g.
        // GetMe) — never manager/recruiter/hr-administrator/company-administrator, which are real
        // role assertions a support session must never carry. This is never resolved through
        // GetEffectiveRolesAsync (there is no role assignment for a support session's marker id).
        if (currentUser.IsSupportSession)
        {
            if (requirement.AllowedRoleIds.Contains(SystemRoles.Employee))
                context.Succeed(requirement);
            return;
        }

        var effectiveRoles = await authorizationService.GetEffectiveRolesAsync(currentUser.UserId.Value);

        if (requirement.AllowedRoleIds.Any(effectiveRoles.Contains))
            context.Succeed(requirement);
    }
}
