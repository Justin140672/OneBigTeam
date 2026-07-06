using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed class Endpoint(ListCandidateDocumentsHandler handler)
    : Endpoint<ListCandidateDocumentsRequest, ListCandidateDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/candidates/{candidateId:guid}/documents");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        ListCandidateDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
