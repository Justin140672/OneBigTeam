using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

internal sealed class Endpoint(
    UpdateSubscriptionPricingConfigHandler handler)
    : Endpoint<UpdateSubscriptionPricingConfigRequest, UpdateSubscriptionPricingConfigResponse>
{
    public override void Configure()
    {
        Put("/api/companies/admin/subscription-pricing-config");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(UpdateSubscriptionPricingConfigRequest req, CancellationToken cancellationToken)
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
