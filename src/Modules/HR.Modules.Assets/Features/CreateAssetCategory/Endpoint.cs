using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed class Endpoint(CreateAssetCategoryHandler handler)
    : Endpoint<CreateAssetCategoryRequest, CreateAssetCategoryResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/asset-categories");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CreateAssetCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/asset-categories/{result.Value!.Id}", result.Value));
    }
}
