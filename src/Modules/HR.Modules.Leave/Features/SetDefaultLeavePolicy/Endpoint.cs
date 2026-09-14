using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.SetDefaultLeavePolicy;

internal sealed class Endpoint(
    SetDefaultLeavePolicyHandler handler) : Endpoint<SetDefaultLeavePolicyRequest>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/leave-policies/{id:guid}/set-default");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        SetDefaultLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
