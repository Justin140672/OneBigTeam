using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ResendVerification;

internal sealed class Endpoint(
    ResendVerificationHandler handler) : Endpoint<ResendVerificationRequest, ResendVerificationResponse>
{
    public override void Configure()
    {
        Post("/api/resend-verification");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ResendVerificationRequest request, CancellationToken cancellationToken)
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
