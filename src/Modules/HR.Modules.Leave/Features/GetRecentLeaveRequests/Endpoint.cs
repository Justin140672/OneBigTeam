using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed class Endpoint(
    GetRecentLeaveRequestsHandler handler,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<GetRecentLeaveRequestsRequest, GetRecentLeaveRequestsResponse>
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Leave cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as GetTeamSicknessToday's SicknessManagePermissionId
    // (HR.Modules.Sickness.Features.GetTeamSicknessToday.Endpoint).
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leave-requests/recent");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetRecentLeaveRequestsRequest request,
        CancellationToken cancellationToken)
    {
        // Viewer identity and HR-administrator status are both resolved server-side from the
        // authenticated caller (JWT "sub" claim + IAuthorizationService), never trusted from the
        // client — same convention as GetMyTeam's managerId resolution and GetTeamSicknessToday's
        // permission check.
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var viewerEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var userId = currentUser.UserId;
        var isHrAdministrator = userId is not null
            && (await authorizationService.GetEffectiveRolesAsync(userId.Value, cancellationToken)).Contains(HrAdministratorRoleId);

        var result = await handler.HandleAsync(request, viewerEmployeeId, isHrAdministrator, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
