using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.DeactivateAssetCategory;

internal sealed class Endpoint(DeactivateAssetCategoryHandler handler)
    : Endpoint<DeactivateAssetCategoryRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/asset-categories/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(DeactivateAssetCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.NoContent());
    }
}
