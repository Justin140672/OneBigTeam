using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.GetAsset;

internal sealed class Endpoint(GetAssetHandler handler)
    : Endpoint<GetAssetRequest, GetAssetResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/assets/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(GetAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
