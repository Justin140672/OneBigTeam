using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetMe;

internal sealed class Endpoint(
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : EndpointWithoutRequest<GetMeResponse>
{
    public override void Configure()
    {
        Get("/api/me");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var companyId))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var permissions = await authorizationService.GetEffectivePermissionsAsync(userId.Value, ct);

        // Mirrors the "company:manage" policy roles exactly (HR.Modules.Identity.IdentityModule.AddRolePolicies)
        // so the Company Settings UI gate matches what the update/read endpoints actually allow.
        var roles = await authorizationService.GetEffectiveRolesAsync(userId.Value, ct);
        var canManageCompany = roles.Contains(SystemRoles.CompanyAdministrator);

        // Role-derived landing/nav flags, additive to CanManageCompany above — CanManageEmployees
        // (computed client-side from PermissionIds) still drives all existing widget gates unchanged.
        var isHrAdministrator = roles.Contains(SystemRoles.HrAdministrator);
        var isManager = roles.Contains(SystemRoles.Manager);
        var isRecruiter = roles.Contains(SystemRoles.Recruiter);

        await Send.ResultAsync(TypedResults.Ok(new GetMeResponse(
            userId.Value,
            companyId,
            currentUser.Email,
            permissions.ToList(),
            canManageCompany,
            isHrAdministrator,
            isManager,
            isRecruiter)));
    }
}

internal sealed record GetMeResponse(
    Guid UserId,
    Guid CompanyId,
    string? Email,
    List<Guid> PermissionIds,
    bool CanManageCompany,
    bool IsHrAdministrator,
    bool IsManager,
    bool IsRecruiter);
