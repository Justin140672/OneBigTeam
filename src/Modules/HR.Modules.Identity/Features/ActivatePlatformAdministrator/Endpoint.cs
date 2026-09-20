using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ActivatePlatformAdministrator;

/// <summary>
/// Deliberately requires only "authenticated by a real Supabase-issued token" (no
/// "platform:admin"/role policy) — the whole point of this endpoint is to grant that status for
/// the first time. See ActivatePlatformAdministratorHandler's remarks for the security reasoning.
/// </summary>
internal sealed class Endpoint(
    ActivatePlatformAdministratorHandler handler,
    ICurrentUser currentUser) : EndpointWithoutRequest<ActivatePlatformAdministratorResponse>
{
    public override void Configure()
    {
        Post("/api/identity/platform-administrators/activate");
        Policies("identity:self-provisioning");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(currentUser, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
