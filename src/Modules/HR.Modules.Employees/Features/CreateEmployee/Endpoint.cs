using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed class Endpoint(
    CreateEmployeeHandler handler) : Endpoint<CreateEmployeeRequest, CreateEmployeeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateEmployeeRequest request,
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

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.Id}",
            result.Value));
    }
}
