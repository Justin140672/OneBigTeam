using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed class Endpoint(
    CreateDepartmentHandler handler) : Endpoint<CreateDepartmentRequest, CreateDepartmentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/departments");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateDepartmentRequest request,
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
            $"/api/companies/{result.Value!.CompanyId}/departments/{result.Value.Id}",
            result.Value));
    }
}
