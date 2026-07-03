using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetTeamSicknessToday;

internal sealed class Endpoint(
    GetTeamSicknessTodayHandler handler,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<GetTeamSicknessTodayRequest, GetTeamSicknessTodayResponse>
{
    // Mirrors HR.Modules.Identity.Domain.SystemPermissions.SicknessManage. Sickness cannot reference
    // Identity's internal SystemPermissions/SystemRoles directly, so the permission id is duplicated
    // here as the sanctioned escape hatch for checking a policy other than the endpoint's own.
    private static readonly Guid SicknessManagePermissionId = new("00000000-0000-0000-0001-000000000015");

    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-sickness-today");
        Policies("sickness:view-team");
    }

    public override async Task HandleAsync(
        GetTeamSicknessTodayRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var authenticatedEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (authenticatedEmployeeId != request.ManagerId)
        {
            var userId = currentUser.UserId;
            var canManageAnyTeam = userId is not null
                && await authorizationService.HasPermissionAsync(userId.Value, SicknessManagePermissionId, cancellationToken);

            if (!canManageAnyTeam)
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
