using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetLeaveRequestAuditHistory;

internal sealed class Endpoint(GetLeaveRequestAuditHistoryHandler handler)
    : EndpointWithoutRequest<GetLeaveRequestAuditHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leave-requests/{leaveRequestId:guid}/audit-history");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId      = Route<Guid>("companyId");
        var leaveRequestId = Route<Guid>("leaveRequestId");

        var result = await handler.HandleAsync(companyId, leaveRequestId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
