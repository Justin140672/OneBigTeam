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

        if (currentUser.IsSupportSession)
        {
            await Send.ResultAsync(TypedResults.Ok(new GetMeResponse(
                userId.Value,
                companyId,
                currentUser.Email,
                [HR.Modules.Identity.Domain.SystemPermissions.EmployeeRead],
                [],
                CanManageCompany: false,
                IsHrAdministrator: false,
                IsManager: false,
                IsRecruiter: false,
                IsEmailConfirmed: true)));
            return;
        }

        var permissions = await authorizationService.GetEffectivePermissionsAsync(userId.Value, ct);

        var roles = await authorizationService.GetEffectiveRolesAsync(userId.Value, ct);
        var canManageCompany = roles.Contains(SystemRoles.CompanyAdministrator);

        var roleIds = roles.ToList();

        var isHrAdministrator = roles.Contains(SystemRoles.HrAdministrator);
        var isManager = roles.Contains(SystemRoles.Manager);
        var isRecruiter = roles.Contains(SystemRoles.Recruiter);

        await Send.ResultAsync(TypedResults.Ok(new GetMeResponse(
            userId.Value,
            companyId,
            currentUser.Email,
            permissions.ToList(),
            roleIds,
            canManageCompany,
            isHrAdministrator,
            isManager,
            isRecruiter,
            IsEmailConfirmed: true)));
    }
}

internal sealed record GetMeResponse(
    Guid UserId,
    Guid CompanyId,
    string? Email,
    List<Guid> PermissionIds,
    List<Guid> RoleIds,
    bool CanManageCompany,
    bool IsHrAdministrator,
    bool IsManager,
    bool IsRecruiter,
    bool IsEmailConfirmed);
