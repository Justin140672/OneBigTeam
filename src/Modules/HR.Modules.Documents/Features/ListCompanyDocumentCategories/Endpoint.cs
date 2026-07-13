using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListCompanyDocumentCategories;

internal sealed class Endpoint(ListCompanyDocumentCategoriesHandler handler)
    : Endpoint<ListCompanyDocumentCategoriesRequest, ListCompanyDocumentCategoriesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/document-categories");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListCompanyDocumentCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
