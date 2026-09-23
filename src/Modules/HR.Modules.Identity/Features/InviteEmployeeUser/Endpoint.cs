using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed class Endpoint(
    InviteEmployeeUserHandler handler,
    ICurrentUser currentUser) : Endpoint<InviteEmployeeUserRequest, InviteEmployeeUserResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/invite-user");
        Policies("users:manage");
    }

    public override async Task HandleAsync(InviteEmployeeUserRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            currentUser.UserId,
            cancellationToken);

        if (result.IsFailure)
        {
            // Ticket 9: canonical translator (same status codes as before) so a rejected public
            // email domain surfaces as 400 with code "work_email_required".
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
