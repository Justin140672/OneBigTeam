using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed class Endpoint(
    GetRecentLeaveRequestsHandler handler) : Endpoint<GetRecentLeaveRequestsRequest, GetRecentLeaveRequestsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leave-requests/recent");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetRecentLeaveRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
