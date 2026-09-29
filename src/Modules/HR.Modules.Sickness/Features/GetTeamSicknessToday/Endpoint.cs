using FastEndpoints;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetTeamSicknessToday;

internal sealed class Endpoint(
    GetTeamSicknessTodayHandler handler,
    ICurrentUser currentUser,
    SicknessResourceAuthorizer resourceAuthorizer) : Endpoint<GetTeamSicknessTodayRequest, GetTeamSicknessTodayResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-sickness-today");
        Policies("sickness:view-team");
    }

    public override async Task HandleAsync(
        GetTeamSicknessTodayRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!await resourceAuthorizer.CanViewManagerTeamAsync(
                request.CompanyId, callerEmployeeId, request.ManagerId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
