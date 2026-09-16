using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.CancelInvite;

internal sealed class Endpoint(
    CancelInviteHandler handler,
    ICurrentUser currentUser) : Endpoint<CancelInviteRequest, CancelInviteResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/invites/{inviteId:guid}/cancel");
        Policies("users:manage");
    }

    public override async Task HandleAsync(CancelInviteRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            currentUser.UserId,
            cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }
            if (result.Error.Code is "conflict" or "concurrency")
            {
                // Ticket 24 (P1): a concurrency conflict (keyed or unkeyed — see
                // CancelInviteHandler.HandleAsync) means a concurrent AcceptInvite already claimed
                // this invite. That is a conflict with current state, not a malformed request, so it
                // must map to 409 the same way the "conflict" branch above already does — never a
                // generic 400 and never an unhandled 500.
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
