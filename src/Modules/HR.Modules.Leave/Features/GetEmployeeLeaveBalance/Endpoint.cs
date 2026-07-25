using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed class Endpoint(
    GetEmployeeLeaveBalanceHandler handler) : Endpoint<GetEmployeeLeaveBalanceRequest, GetEmployeeLeaveBalanceResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-balances");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetEmployeeLeaveBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
