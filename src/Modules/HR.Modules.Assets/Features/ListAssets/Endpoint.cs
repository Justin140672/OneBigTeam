using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.ListAssets;

internal sealed class Endpoint(ListAssetsHandler handler)
    : Endpoint<ListAssetsRequest, List<ListAssetsResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/assets");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ListAssetsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
