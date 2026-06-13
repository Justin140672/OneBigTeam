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

        var effectiveRoles = await authorizationService.GetEffectiveRolesAsync(currentUser.UserId.Value);

        if (requirement.AllowedRoleIds.Any(effectiveRoles.Contains))
            context.Succeed(requirement);
    }
}
