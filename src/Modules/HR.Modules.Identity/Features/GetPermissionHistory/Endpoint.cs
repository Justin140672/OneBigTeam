using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetPermissionHistory;

// IAM-08: consolidated, company-scoped permission-change history — direct role changes,
// position/inherited-role changes and override changes all surface through the same view (unlike
// GetUserAuditHistory, which is scoped to a single employee).
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
