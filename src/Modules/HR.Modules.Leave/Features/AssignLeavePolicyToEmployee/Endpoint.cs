using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed class Endpoint(
    AssignLeavePolicyToEmployeeHandler handler, ICurrentUser currentUser) : Endpoint<AssignLeavePolicyToEmployeeRequest, AssignLeavePolicyToEmployeeResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-policy");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        AssignLeavePolicyToEmployeeRequest request,
        CancellationToken cancellationToken)
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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
