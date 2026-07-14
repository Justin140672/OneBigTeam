using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;

internal sealed class Endpoint(GetSharedCompanyDocumentAcknowledgementProgressHandler handler)
    : Endpoint<GetSharedCompanyDocumentAcknowledgementProgressRequest, GetSharedCompanyDocumentAcknowledgementProgressResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/acknowledgement-progress");
        Policies("shared-document:view-acknowledgement-status");
    }

    public override async Task HandleAsync(
        GetSharedCompanyDocumentAcknowledgementProgressRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
