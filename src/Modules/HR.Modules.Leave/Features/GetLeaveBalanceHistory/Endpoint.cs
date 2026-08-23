using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed class Endpoint(
    GetLeaveBalanceHistoryHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<GetLeaveBalanceHistoryRequest, GetLeaveBalanceHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-types/{leaveTypeId:guid}/balance-history");
        // LEAVE-01: the coarse "leave:manage" policy previously blocked employees from ever
        // reaching their own balance history, contradicting the requirement that an employee's
        // own leave balance (and its history) is visible to them. The endpoint-level policy now
        // only proves tenant/role membership; resource scope is enforced below via
        // LeaveResourceAuthorizer.CanViewAsync, matching GetEmployeeLeaveBalance/Endpoint.cs.
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetLeaveBalanceHistoryRequest request,
        CancellationToken cancellationToken)
    {
        // LEAVE-01: self, manager-in-hierarchy, or HR Administrator may view.
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!await authorizer.CanViewAsync(request.CompanyId, callerId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
