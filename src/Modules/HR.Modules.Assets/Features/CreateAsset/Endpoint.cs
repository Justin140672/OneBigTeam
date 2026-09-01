using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class Endpoint(CreateAssetHandler handler)
    : Endpoint<CreateAssetRequest, CreateAssetResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/assets");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CreateAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code switch
            {
                "not_found" => StatusCodes.Status404NotFound,
                // A shape/requiredness failure the shape-only validator can't catch (e.g. the
                // AssetNumberMode-dependent "asset number is required") is a 422, not a 409.
                "validation" => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status409Conflict,
            };
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/assets/{result.Value!.Id}", result.Value));
    }
}
