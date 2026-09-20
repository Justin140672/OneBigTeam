using Microsoft.AspNetCore.Builder;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.Login;

internal sealed class Endpoint(
    LoginHandler handler) : Endpoint<LoginRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("/api/login");
        AllowAnonymous();
        // Policy name must match HR.Api.RateLimiting.IdentityRateLimiting.LoginPolicy — kept as a
        // literal (not a cross-project constant reference) because modules must never reference
        // HR.Api (see 02-module-boundaries.md); the rate-limit middleware itself lives in HR.Api,
        // which owns the request pipeline.
        Options(b => b.RequireRateLimiting("identity-login"));
    }

    public override async Task HandleAsync(LoginRequest request, CancellationToken cancellationToken)
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
