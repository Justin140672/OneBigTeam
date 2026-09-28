using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.DownloadCandidateDocument;

internal sealed class Endpoint(DownloadCandidateDocumentHandler handler)
    : Endpoint<DownloadCandidateDocumentRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/candidates/{candidateId:guid}/documents/{documentId:guid}/download");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        DownloadCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var body = new { error = result.Error.Message, code = result.Error.Code };

            // [P1] Not yet scanned → 409 (retry later); quarantined or unscannable → 403 (denied).
            IResult response = result.Error.Code switch
            {
                DownloadCandidateDocumentHandler.ScanPendingCode => TypedResults.Conflict(body),
                DownloadCandidateDocumentHandler.QuarantinedCode or DownloadCandidateDocumentHandler.ScanFailedCode =>
                    TypedResults.Json(body, statusCode: StatusCodes.Status403Forbidden),
                _ => TypedResults.NotFound(new { error = result.Error.Message }),
            };

            await Send.ResultAsync(response);
            return;
        }

        await Send.RedirectAsync(result.Value!.ToString(), isPermanent: false, allowRemoteRedirects: true);
    }
}
