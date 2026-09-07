using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed class Endpoint(SetMarketingFeaturePublicationHandler handler)
    : Endpoint<SetMarketingFeaturePublicationRequest, SetMarketingFeaturePublicationResponse>
{
    public override void Configure()
    {
        Put("/api/marketing/admin/features/{id:guid}/publication");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(SetMarketingFeaturePublicationRequest req, CancellationToken cancellationToken)
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
