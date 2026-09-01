using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetPublicSubscriptionPricing;

internal sealed class Endpoint(
    GetPublicSubscriptionPricingHandler handler)
    : Endpoint<GetPublicSubscriptionPricingRequest, GetPublicSubscriptionPricingResponse>
{
    public override void Configure()
    {
        Get("/api/public/subscription-pricing");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetPublicSubscriptionPricingRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
