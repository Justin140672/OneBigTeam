using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

internal sealed class Endpoint(
    CreatePlatformAdministratorHandler handler,
    ICurrentUser currentUser) : Endpoint<CreatePlatformAdministratorRequest, CreatePlatformAdministratorResponse>
{
    public override void Configure()
    {
        Post("/api/platform-administrators");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CreatePlatformAdministratorRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            currentUser,
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
