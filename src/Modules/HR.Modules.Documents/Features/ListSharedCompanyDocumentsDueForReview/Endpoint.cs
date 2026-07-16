using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;

internal sealed class Endpoint(ListSharedCompanyDocumentsDueForReviewHandler handler)
    : Endpoint<ListSharedCompanyDocumentsDueForReviewRequest, ListSharedCompanyDocumentsDueForReviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/due-for-review");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        ListSharedCompanyDocumentsDueForReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
