using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateLocation;

internal sealed class Endpoint(
    CreateLocationHandler handler) : Endpoint<CreateLocationRequest, CreateLocationResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/locations");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateLocationRequest request,
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
            $"/api/companies/{result.Value!.CompanyId}/locations/{result.Value.Id}",
            result.Value));
    }
}
