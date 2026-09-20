using Microsoft.AspNetCore.Builder;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ResetPassword;

internal sealed class Endpoint(
    ResetPasswordHandler handler) : Endpoint<ResetPasswordRequest, ResetPasswordResponse>
{
    public override void Configure()
    {
        Post("/api/reset-password");
        AllowAnonymous();
        // See HR.Modules.Identity.Features.Login.Endpoint's remarks on why this is a literal.
        Options(b => b.RequireRateLimiting("identity-reset-password"));
    }

    public override async Task HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };
            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
