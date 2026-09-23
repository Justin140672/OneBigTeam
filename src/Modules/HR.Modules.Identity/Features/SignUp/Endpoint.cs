using Microsoft.AspNetCore.Builder;
using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.SignUp;

// Public (anonymous) self-service signup — the only path today that creates a brand-new Company +
// admin user without an already-authenticated caller. See Handler.cs remarks for the local-auth
// approach (mirrors AcceptInvite) and cross-module transaction caveat.
internal sealed class Endpoint(
    SignUpHandler handler) : Endpoint<SignUpRequest, SignUpResponse>
{
    public override void Configure()
    {
        Post("/api/signup");
        AllowAnonymous();
        // See HR.Modules.Identity.Features.Login.Endpoint's remarks on why this is a literal.
        Options(b => b.RequireRateLimiting("identity-signup"));
    }

    public override async Task HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            // Ticket 9: routed through the canonical translator so the body carries the
            // machine-readable code (e.g. "work_email_required" -> 400) that the marketing
            // /signup-submit proxy uses to mark the email field invalid. Status codes are unchanged
            // (conflict -> 409, everything else -> 400).
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
