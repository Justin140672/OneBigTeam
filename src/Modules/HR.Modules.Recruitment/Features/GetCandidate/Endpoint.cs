using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetCandidate;

internal sealed class Endpoint(GetCandidateHandler handler)
    : Endpoint<GetCandidateRequest, GetCandidateResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/candidates/{candidateId:guid}");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
