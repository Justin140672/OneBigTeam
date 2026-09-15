using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.CancelSubscription;

internal sealed class Endpoint(
    CancelSubscriptionHandler handler) : EndpointWithoutRequest<CancelSubscriptionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/subscription/cancel");
        Policies("subscription:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            cancellationToken, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
