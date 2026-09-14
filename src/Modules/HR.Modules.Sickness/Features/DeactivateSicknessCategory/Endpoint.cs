using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.DeactivateSicknessCategory;

internal sealed class Endpoint(DeactivateSicknessCategoryHandler handler, ICurrentUser currentUser)
    : Endpoint<DeactivateSicknessCategoryRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/sickness-categories/{id:guid}");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(DeactivateSicknessCategoryRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with
            {
                ActorEmployeeId = currentUser.UserId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            },
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
