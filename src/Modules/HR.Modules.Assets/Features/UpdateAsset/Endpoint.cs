using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.UpdateAsset;

internal sealed class Endpoint(UpdateAssetHandler handler)
    : Endpoint<UpdateAssetRequest, UpdateAssetResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/assets/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code == "conflict"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status404NotFound;
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
