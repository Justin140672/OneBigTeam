using FastEndpoints;
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
    }

    public override async Task HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
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
