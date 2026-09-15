using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

internal sealed class Endpoint(RetryEmployeeRenumberSideEffectHandler handler)
    : Endpoint<RetryEmployeeRenumberSideEffectRequest, RetryEmployeeRenumberSideEffectResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employee-renumber-side-effects/{outboxMessageId:guid}/retry");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        RetryEmployeeRenumberSideEffectRequest request,
        CancellationToken cancellationToken)
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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
