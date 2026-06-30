using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.ListAssetCategories;

internal sealed class Endpoint(ListAssetCategoriesHandler handler)
    : Endpoint<ListAssetCategoriesRequest, List<ListAssetCategoriesResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/asset-categories");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ListAssetCategoriesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
