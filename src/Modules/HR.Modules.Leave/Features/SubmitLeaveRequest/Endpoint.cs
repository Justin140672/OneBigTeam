using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed class Endpoint(
    SubmitLeaveRequestHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<SubmitLeaveRequestRequest, SubmitLeaveRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests");
        Policies("leave:request");
    }

    public override async Task HandleAsync(
        SubmitLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        // LEAVE-01: submitting leave is self-service only — an employee can never submit on
        // behalf of another employee. HR Administrators retain the override.
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
            // P2 (Ticket 4 follow-up): "concurrency" and "conflict" both need to reach the caller
            // as 409, matching ApproveLeaveRequest/Endpoint.cs — ProblemResults.FromError already
            // maps both codes to 409 (distinguishable via the `code` field), so this now matches
            // the shared translator exactly.
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.EmployeeId}/leave-requests/{result.Value.Id}",
            result.Value));
    }
}
