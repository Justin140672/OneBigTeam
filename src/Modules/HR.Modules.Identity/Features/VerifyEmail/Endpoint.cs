using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.VerifyEmail;

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
            var businessError = new { code = result.Error.Code, error = result.Error.Message };
            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
