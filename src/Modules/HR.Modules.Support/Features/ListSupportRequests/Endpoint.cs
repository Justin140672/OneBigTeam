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
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        if (currentUser.UserId is not Guid userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var requestWithFilter = request with { RequestorUserId = userId };
        var result = await handler.HandleAsync(requestWithFilter, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
