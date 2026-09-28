using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.ListSupportRequests;

internal sealed class Endpoint(ListSupportRequestsHandler handler, ICurrentUser currentUser)
    : Endpoint<ListSupportRequestsRequest, List<ListSupportRequestsResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/support/requests");
        Policies("support:request");
    }

    public override async Task HandleAsync(ListSupportRequestsRequest request, CancellationToken cancellationToken)
    {
        // Verify the caller belongs to the company in the route
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // Self-service: only list the requestor's own support requests
        if (currentUser.UserId is not Guid userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Add the requestor filter to the request for the handler to use
        var requestWithFilter = request with { RequestorUserId = userId };
        var result = await handler.HandleAsync(requestWithFilter, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
