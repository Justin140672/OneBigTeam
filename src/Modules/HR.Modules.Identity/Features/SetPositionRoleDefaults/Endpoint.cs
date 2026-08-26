using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.SetPositionRoleDefaults;

internal sealed class Endpoint(
    SetPositionRoleDefaultsHandler handler,
    ICurrentUser currentUser) : Endpoint<SetPositionRoleDefaultsRequest, SetPositionRoleDefaultsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/positions/{positionProfileId:guid}/role-defaults");
        Policies("users:manage");
    }

    public override async Task HandleAsync(SetPositionRoleDefaultsRequest request, CancellationToken cancellationToken)
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
