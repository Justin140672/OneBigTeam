using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ListUsers;

internal sealed class Endpoint(ListUsersHandler handler) : Endpoint<ListUsersRequest, ListUsersResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users");
        Policies("users:view");
    }

    public override async Task HandleAsync(ListUsersRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
