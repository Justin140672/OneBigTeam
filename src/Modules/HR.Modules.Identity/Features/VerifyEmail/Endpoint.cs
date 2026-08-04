using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.VerifyEmail;

// Called by HR.Web's /verify-email callback (never directly by the browser) once it already holds
// a real Supabase access token — see Handler.cs remarks. Authenticated via the normal JWT Bearer
// pipeline (Policies("role:employee")), not anonymous: the caller's Authorization header carries
// the Supabase-issued token itself.
internal sealed class Endpoint(
    VerifyEmailHandler handler) : EndpointWithoutRequest<VerifyEmailResponse>
{
    public override void Configure()
    {
        Post("/api/verify-email");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        if (result.IsFailure)
        {
            // HR.Web needs the error Code (not just Message) to distinguish "invalid_or_expired"
            // from any other failure and route accordingly.
            var businessError = new { code = result.Error.Code, error = result.Error.Message };
            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
