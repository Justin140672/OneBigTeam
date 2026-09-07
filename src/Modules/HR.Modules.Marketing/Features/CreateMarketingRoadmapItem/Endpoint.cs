using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed class Endpoint(CreateMarketingRoadmapItemHandler handler)
    : Endpoint<CreateMarketingRoadmapItemRequest, CreateMarketingRoadmapItemResponse>
{
    public override void Configure()
    {
        Post("/api/marketing/admin/roadmap");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CreateMarketingRoadmapItemRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status400BadRequest));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/marketing/admin/roadmap/{result.Value!.Id}", result.Value));
    }
}
