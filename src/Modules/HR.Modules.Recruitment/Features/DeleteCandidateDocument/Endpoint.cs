using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.DeleteCandidateDocument;

internal sealed class Endpoint(DeleteCandidateDocumentHandler handler)
    : Endpoint<DeleteCandidateDocumentRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/candidates/{candidateId:guid}/documents/{documentId:guid}");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        DeleteCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
