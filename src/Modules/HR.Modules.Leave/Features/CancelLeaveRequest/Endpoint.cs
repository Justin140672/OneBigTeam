using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.CancelLeaveRequest;

internal sealed class Endpoint(
    CancelLeaveRequestHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<CancelLeaveRequestRequest, CancelLeaveRequestResponse>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/{leaveRequestId:guid}");
        Policies("leave:request");
    }

    public override async Task HandleAsync(
        CancelLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        // LEAVE-01: cancel is self-service only, same scope as submit/preview.
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!await authorizer.CanActOnOwnLeaveAsync(callerId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            // P1 #4: routes "concurrency" (as well as "conflict") to 409, matching every other
            // versioned-aggregate endpoint (see ProblemResults.FromError).
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
