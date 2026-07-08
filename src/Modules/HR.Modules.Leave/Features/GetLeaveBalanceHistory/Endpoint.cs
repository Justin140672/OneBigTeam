using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed class Endpoint(
    GetLeaveBalanceHistoryHandler handler) : Endpoint<GetLeaveBalanceHistoryRequest, GetLeaveBalanceHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-types/{leaveTypeId:guid}/balance-history");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        GetLeaveBalanceHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
