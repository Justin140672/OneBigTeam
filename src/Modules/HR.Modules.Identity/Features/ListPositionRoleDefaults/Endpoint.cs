using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.ListPositionRoleDefaults;

internal sealed class Endpoint(ListPositionRoleDefaultsHandler handler)
    : Endpoint<ListPositionRoleDefaultsRequest, ListPositionRoleDefaultsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/positions/role-defaults");
        Policies("users:manage");
    }

    public override async Task HandleAsync(ListPositionRoleDefaultsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
