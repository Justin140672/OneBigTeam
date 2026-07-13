using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.CreateCompanyDocumentCategory;

internal sealed class Endpoint(CreateCompanyDocumentCategoryHandler handler)
    : Endpoint<CreateCompanyDocumentCategoryRequest, CreateCompanyDocumentCategoryResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/document-categories");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        CreateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/document-categories/{result.Value.Id}",
            result.Value));
    }
}
