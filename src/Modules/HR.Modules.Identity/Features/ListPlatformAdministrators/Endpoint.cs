using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ListPlatformAdministrators;

internal sealed class Endpoint(
    ListPlatformAdministratorsHandler handler,
    ICurrentUser currentUser) : Endpoint<ListPlatformAdministratorsRequest, ListPlatformAdministratorsResponse>
{
    public override void Configure()
    {
        Get("/api/platform-administrators");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ListPlatformAdministratorsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(currentUser, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };
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
