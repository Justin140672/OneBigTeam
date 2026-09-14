using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.DeactivateAssetCategory;

internal sealed class Endpoint(DeactivateAssetCategoryHandler handler)
    : Endpoint<DeactivateAssetCategoryRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/asset-categories/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(DeactivateAssetCategoryRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.NoContent());
    }
}
