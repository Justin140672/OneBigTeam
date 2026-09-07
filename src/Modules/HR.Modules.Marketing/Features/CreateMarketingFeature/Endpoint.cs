using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed class Endpoint(CreateMarketingFeatureHandler handler)
    : Endpoint<CreateMarketingFeatureRequest, CreateMarketingFeatureResponse>
{
    public override void Configure()
    {
        Post("/api/marketing/admin/features");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CreateMarketingFeatureRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code == "conflict"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/marketing/admin/features/{result.Value!.Id}", result.Value));
    }
}
