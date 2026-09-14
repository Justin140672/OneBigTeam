using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.ListSupportRequests;

// Ticket 17: platform-support surface for HR.Admin.Web. Reuses the exact same handler as the
// tenant "support:manage" endpoint above (query, tenant-boundary and shaping behaviour are
// therefore always identical) but is gated by "platform:admin" instead, so an enabled Platform
// Administrator can list any company's support requests without being granted a tenant HR role.
// See PlatformAdminAuthorizationHandler and TenantRouteAuthorizationMiddleware's "platform:admin"
// exemption for why a {companyId} route segment is safe here despite the caller having no tenant.
internal sealed class AdminEndpoint(ListSupportRequestsHandler handler)
    : Endpoint<ListSupportRequestsRequest, List<ListSupportRequestsResponse>>
{
    public override void Configure()
    {
        Get("/api/admin/companies/{companyId:guid}/support/requests");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ListSupportRequestsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
