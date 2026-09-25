using FastEndpoints;
using HR.SharedKernel;
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
            // not_found -> 404; conflict (CV still referenced by an application) -> 409.
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
