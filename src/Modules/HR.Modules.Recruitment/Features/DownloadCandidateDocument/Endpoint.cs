using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.DownloadCandidateDocument;

internal sealed class Endpoint(DownloadCandidateDocumentHandler handler)
    : Endpoint<DownloadCandidateDocumentRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/candidates/{candidateId:guid}/documents/{documentId:guid}/download");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        DownloadCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.RedirectAsync(result.Value!.ToString(), isPermanent: false, allowRemoteRedirects: true);
    }
}
