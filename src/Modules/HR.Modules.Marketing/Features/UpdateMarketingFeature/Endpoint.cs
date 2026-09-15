using FastEndpoints;

using HR.SharedKernel;

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
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            req with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
