using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAuditHistory;

internal sealed class Endpoint(GetSharedCompanyDocumentAuditHistoryHandler handler)
    : EndpointWithoutRequest<GetSharedCompanyDocumentAuditHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/audit-history");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId  = Route<Guid>("companyId");
        var documentId = Route<Guid>("documentId");

        var result = await handler.HandleAsync(companyId, documentId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
