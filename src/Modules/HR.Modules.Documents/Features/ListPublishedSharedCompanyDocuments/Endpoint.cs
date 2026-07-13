using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed class Endpoint(ListPublishedSharedCompanyDocumentsHandler handler)
    : Endpoint<ListPublishedSharedCompanyDocumentsRequest, ListPublishedSharedCompanyDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/published");
        Policies("shared-document:view-published");
    }

    public override async Task HandleAsync(
        ListPublishedSharedCompanyDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
