using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed class Endpoint(DeactivateLeaveTypeHandler handler, ICurrentUser currentUser)
    : Endpoint<DeactivateLeaveTypeRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/leave-types/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(DeactivateLeaveTypeRequest request, CancellationToken cancellationToken)
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
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }
            // NOTE: pre-existing business "conflict" errors (already inactive, system type, in use)
            // deliberately stay mapped to 400 here to preserve existing API contract/tests. An
            // idempotency-key reuse (also Error.Conflict) therefore also surfaces as 400 rather
            // than 409 for this endpoint specifically.
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }
        await Send.NoContentAsync(cancellationToken);
    }
}
