using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.SubmitLeaveRequestDraft;

internal sealed class Endpoint(
    SubmitLeaveRequestDraftHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<SubmitLeaveRequestDraftRequest, SubmitLeaveRequestDraftResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/{leaveRequestId:guid}/submit");
        Policies("leave:request");
    }

    public override async Task HandleAsync(
        SubmitLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            // P2 (Ticket 4 follow-up): routes "concurrency" (as well as "conflict") to 409, matching
            // ApproveLeaveRequest/Endpoint.cs - a losing auto-approval save now returns
            // Error.Concurrency rather than throwing, and clients must see the same retryable 409
            // shape they'd get from a manual-approval conflict.
            if (result.Error.Code is "conflict" or "concurrency")
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
