using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;

internal sealed class Endpoint(UpdateCompanyDocumentCategoryHandler handler)
    : Endpoint<UpdateCompanyDocumentCategoryRequest, UpdateCompanyDocumentCategoryResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/document-categories/{categoryId:guid}");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        UpdateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
