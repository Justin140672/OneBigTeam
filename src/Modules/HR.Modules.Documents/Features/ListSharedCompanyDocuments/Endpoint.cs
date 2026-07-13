using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed class Endpoint(ListSharedCompanyDocumentsHandler handler)
    : Endpoint<ListSharedCompanyDocumentsRequest, ListSharedCompanyDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        ListSharedCompanyDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
