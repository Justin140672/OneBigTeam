using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.RequestPasswordReset;

internal sealed class Endpoint(
    RequestPasswordResetHandler handler) : Endpoint<RequestPasswordResetRequest, RequestPasswordResetResponse>
{
    public override void Configure()
    {
        Post("/api/forgot-password");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken)
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
