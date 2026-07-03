using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed class Endpoint(
    GetMissingFitNotesHandler handler) : Endpoint<GetMissingFitNotesRequest, GetMissingFitNotesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/sickness-evidence-requests/missing");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(
        GetMissingFitNotesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
