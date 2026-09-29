using FastEndpoints;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.ListAttendanceAlerts;

internal sealed class Endpoint(
    ListAttendanceAlertsHandler handler,
    ICurrentUser currentUser,
    SicknessResourceAuthorizer authorizer) : Endpoint<ListAttendanceAlertsRequest, ListAttendanceAlertsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/attendance-alerts");
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        ListAttendanceAlertsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var isHrAdministrator = await authorizer.IsHrAdministratorAsync(callerId, cancellationToken);
        var authorizedEmployeeIds = await authorizer.GetAuthorizedEmployeeIdsAsync(
            request.CompanyId, callerId, cancellationToken);

        var response = await handler.HandleAsync(request, authorizedEmployeeIds, isHrAdministrator, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
