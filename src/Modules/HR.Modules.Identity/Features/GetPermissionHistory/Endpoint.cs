using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetPermissionHistory;

internal sealed class Endpoint(GetPermissionHistoryHandler handler) : Endpoint<GetPermissionHistoryRequest, GetPermissionHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/permission-history");
        Policies("users:view");
    }

    public override async Task HandleAsync(GetPermissionHistoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
