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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
