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
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetRecentLeaveRequestsRequest request,
        CancellationToken cancellationToken)
    {
        // Viewer identity and HR-administrator status are both resolved server-side from
        // ICurrentUser.UserId (this app's resolved Employee/UserId, NOT the raw Supabase "sub"
        // claim — see GetMyEmployee/Endpoint.cs) + IAuthorizationService, never trusted from the
        // client — same convention as GetMyTeam's managerId resolution and GetTeamSicknessToday's
        // permission check.
        if (currentUser.UserId is not { } viewerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var isHrAdministrator =
            (await authorizationService.GetEffectiveRolesAsync(viewerEmployeeId, cancellationToken)).Contains(HrAdministratorRoleId);

        var result = await handler.HandleAsync(request, viewerEmployeeId, isHrAdministrator, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
