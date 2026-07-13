using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocument;

internal sealed class Endpoint(GetSharedCompanyDocumentHandler handler)
    : Endpoint<GetSharedCompanyDocumentRequest, GetSharedCompanyDocumentResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        GetSharedCompanyDocumentRequest request,
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
