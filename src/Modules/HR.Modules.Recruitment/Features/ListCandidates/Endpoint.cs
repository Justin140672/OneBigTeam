using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListCandidates;

internal sealed class Endpoint(ListCandidatesHandler handler)
    : Endpoint<ListCandidatesRequest, ListCandidatesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/candidates");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
