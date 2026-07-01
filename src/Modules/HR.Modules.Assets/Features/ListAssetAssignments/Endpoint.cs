using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.ListAssetAssignments;

internal sealed class Endpoint(ListAssetAssignmentsHandler handler)
    : Endpoint<ListAssetAssignmentsRequest, List<ListAssetAssignmentsResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/assets/{assetId:guid}/assignments");
        Policies("asset:view");
    }

    public override async Task HandleAsync(ListAssetAssignmentsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
