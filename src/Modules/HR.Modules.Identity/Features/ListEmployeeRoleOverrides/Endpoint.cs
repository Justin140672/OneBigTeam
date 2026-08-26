using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ListEmployeeRoleOverrides;

internal sealed class Endpoint(ListEmployeeRoleOverridesHandler handler)
    : Endpoint<ListEmployeeRoleOverridesRequest, ListEmployeeRoleOverridesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/{userId:guid}/role-overrides");
        Policies("users:manage");
    }

    public override async Task HandleAsync(ListEmployeeRoleOverridesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
