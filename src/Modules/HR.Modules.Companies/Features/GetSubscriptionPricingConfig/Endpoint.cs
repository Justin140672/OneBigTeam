using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetSubscriptionPricingConfig;

internal sealed class Endpoint(
    GetSubscriptionPricingConfigHandler handler)
    : Endpoint<GetSubscriptionPricingConfigRequest, GetSubscriptionPricingConfigResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/subscription-pricing-config");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetSubscriptionPricingConfigRequest req, CancellationToken cancellationToken)
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
