using FastEndpoints;
using HR.Modules.Employees.Features.GetMyEqualityData;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.SaveMyEqualityData;

internal sealed class Endpoint(SaveMyEqualityDataHandler handler, ICurrentUser currentUser)
    : Endpoint<SaveMyEqualityDataRequest, GetMyEqualityDataResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/equality-record");
        Policies("role:employee");
    }

    public override async Task HandleAsync(SaveMyEqualityDataRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } authenticatedEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (authenticatedEmployeeId != request.EmployeeId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure && result.Error.Code == "conflict")
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
