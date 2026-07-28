using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.UpdateUserRoles;

internal sealed class Endpoint(
    UpdateUserRolesHandler handler,
    ICurrentUser currentUser) : Endpoint<UpdateUserRolesRequest, UpdateUserRolesResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/users/{userId:guid}/roles");
        Policies("users:manage");
    }

    public override async Task HandleAsync(UpdateUserRolesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, currentUser.UserId, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }
            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
