using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.UpdateAssetCategory;

internal sealed class Endpoint(UpdateAssetCategoryHandler handler)
    : Endpoint<UpdateAssetCategoryRequest, UpdateAssetCategoryResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/asset-categories/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(UpdateAssetCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
