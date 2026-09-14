using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.ResumeSubscription;

internal sealed class Endpoint(
    ResumeSubscriptionHandler handler) : EndpointWithoutRequest<ResumeSubscriptionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/subscription/resume");
        Policies("subscription:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            cancellationToken, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
