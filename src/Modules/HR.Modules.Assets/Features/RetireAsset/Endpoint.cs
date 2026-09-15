using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.RetireAsset;

internal sealed class Endpoint(RetireAssetHandler handler)
    : Endpoint<RetireAssetRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/assets/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(RetireAssetRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.NoContent());
    }
}
