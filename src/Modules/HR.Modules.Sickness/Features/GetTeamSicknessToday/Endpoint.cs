using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetTeamSicknessToday;

internal sealed class Endpoint(
    GetTeamSicknessTodayHandler handler) : Endpoint<GetTeamSicknessTodayRequest, GetTeamSicknessTodayResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-sickness-today");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetTeamSicknessTodayRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
