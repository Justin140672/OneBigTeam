using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed class Endpoint(
    GetRecentLeaveRequestsHandler handler,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<GetRecentLeaveRequestsRequest, GetRecentLeaveRequestsResponse>
{
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
