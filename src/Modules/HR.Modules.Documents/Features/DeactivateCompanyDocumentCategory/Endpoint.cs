using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DeactivateCompanyDocumentCategory;

internal sealed class Endpoint(DeactivateCompanyDocumentCategoryHandler handler)
    : Endpoint<DeactivateCompanyDocumentCategoryRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/document-categories/{categoryId:guid}");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        DeactivateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
