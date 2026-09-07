using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed class Endpoint(SetMarketingRoadmapItemPublicationHandler handler)
    : Endpoint<SetMarketingRoadmapItemPublicationRequest, SetMarketingRoadmapItemPublicationResponse>
{
    public override void Configure()
    {
        Put("/api/marketing/admin/roadmap/{id:guid}/publication");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(SetMarketingRoadmapItemPublicationRequest req, CancellationToken cancellationToken)
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
