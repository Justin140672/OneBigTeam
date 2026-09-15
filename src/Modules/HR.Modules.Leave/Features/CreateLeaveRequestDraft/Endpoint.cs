using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.CreateLeaveRequestDraft;

internal sealed class Endpoint(
    CreateLeaveRequestDraftHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<CreateLeaveRequestDraftRequest, CreateLeaveRequestDraftResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests/drafts");
        Policies("leave:request");
    }

    public override async Task HandleAsync(
        CreateLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
        // LEAVE-07: drafts are self-service only, same scope as submit - see LEAVE-01.
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

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.EmployeeId}/leave-requests/{result.Value.Id}",
            result.Value));
    }
}
