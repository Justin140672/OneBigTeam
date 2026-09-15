using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.UpdateLeaveRequestDraft;

internal sealed class Endpoint(
    UpdateLeaveRequestDraftHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<UpdateLeaveRequestDraftRequest, UpdateLeaveRequestDraftResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/{leaveRequestId:guid}/draft");
        Policies("leave:request");
    }

    public override async Task HandleAsync(
        UpdateLeaveRequestDraftRequest request,
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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
