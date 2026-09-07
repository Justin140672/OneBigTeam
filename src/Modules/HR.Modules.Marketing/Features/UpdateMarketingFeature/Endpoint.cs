using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.UpdateMarketingFeature;

internal sealed class Endpoint(UpdateMarketingFeatureHandler handler)
    : Endpoint<UpdateMarketingFeatureRequest, UpdateMarketingFeatureResponse>
{
    public override void Configure()
    {
        Put("/api/marketing/admin/features/{id:guid}");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(UpdateMarketingFeatureRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code switch
            {
                "not_found" => StatusCodes.Status404NotFound,
                "conflict" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
