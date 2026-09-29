using FastEndpoints;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed class Endpoint(
    GetEmployeeTasksHandler handler,
    ICurrentUser currentUser,
    TasksResourceAuthorizer resourceAuthorizer) : Endpoint<GetEmployeeTasksRequest, GetEmployeeTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/tasks");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetEmployeeTasksRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        if (!await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                request.CompanyId, callerEmployeeId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
