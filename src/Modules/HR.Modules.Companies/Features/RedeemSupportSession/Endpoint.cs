using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.RedeemSupportSession;

internal sealed class Endpoint(
    RedeemSupportSessionHandler handler) : Endpoint<RedeemSupportSessionRequest, RedeemSupportSessionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/admin/support-session/redeem");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RedeemSupportSessionRequest req, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            req with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

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
