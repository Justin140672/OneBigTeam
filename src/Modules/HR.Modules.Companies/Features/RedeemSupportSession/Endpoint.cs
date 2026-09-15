using FastEndpoints;
using HR.SharedKernel;

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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
