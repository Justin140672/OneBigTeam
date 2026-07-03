using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetCurrentSicknessAbsences;

internal sealed class Endpoint(
    GetCurrentSicknessAbsencesHandler handler) : Endpoint<GetCurrentSicknessAbsencesRequest, GetCurrentSicknessAbsencesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/sickness-records/current");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(
        GetCurrentSicknessAbsencesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
