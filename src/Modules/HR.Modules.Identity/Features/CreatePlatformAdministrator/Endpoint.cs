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
        var result = await handler.HandleAsync(request, currentUser, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }
            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }
            await Send.ResultAsync(TypedResults.BadRequest(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
