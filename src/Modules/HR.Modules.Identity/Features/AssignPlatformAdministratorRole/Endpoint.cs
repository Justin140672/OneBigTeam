using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed class Endpoint(
    AssignPlatformAdministratorRoleHandler handler,
    ICurrentUser currentUser) : Endpoint<AssignPlatformAdministratorRoleRequest, AssignPlatformAdministratorRoleResponse>
{
    public override void Configure()
    {
        Post("/api/platform-administrators/{id:guid}/role");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(AssignPlatformAdministratorRoleRequest request, CancellationToken cancellationToken)
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
