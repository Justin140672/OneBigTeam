using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.AssignManager;

internal sealed class Endpoint(
    AssignManagerHandler handler) : Endpoint<AssignManagerRequest, AssignManagerResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{id:guid}/manager");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        AssignManagerRequest request,
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
