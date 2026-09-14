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
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            req with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
