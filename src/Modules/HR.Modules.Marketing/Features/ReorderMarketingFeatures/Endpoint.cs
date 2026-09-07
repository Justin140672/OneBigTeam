using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed class Endpoint(ReorderMarketingFeaturesHandler handler)
    : Endpoint<ReorderMarketingFeaturesRequest, ReorderMarketingFeaturesResponse>
{
    public override void Configure()
    {
        Put("/api/marketing/admin/features/order");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ReorderMarketingFeaturesRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code == "not_found"
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
