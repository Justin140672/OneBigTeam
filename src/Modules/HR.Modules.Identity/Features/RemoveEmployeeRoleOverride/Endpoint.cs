using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.RemoveEmployeeRoleOverride;

internal sealed class Endpoint(
    RemoveEmployeeRoleOverrideHandler handler,
    ICurrentUser currentUser) : Endpoint<RemoveEmployeeRoleOverrideRequest, RemoveEmployeeRoleOverrideResponse>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/users/{userId:guid}/role-overrides/{roleId:guid}");
        Policies("users:manage");
    }

    public override async Task HandleAsync(RemoveEmployeeRoleOverrideRequest request, CancellationToken cancellationToken)
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
            if (result.Error.Code == "forbidden")
            {
                await Send.ResultAsync(Results.Json(error, statusCode: StatusCodes.Status403Forbidden));
                return;
            }
            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
