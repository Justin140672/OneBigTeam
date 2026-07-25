using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.ListLeaveRequests;

internal sealed class Endpoint(
    ListLeaveRequestsHandler handler) : Endpoint<ListLeaveRequestsRequest, ListLeaveRequestsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListLeaveRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
