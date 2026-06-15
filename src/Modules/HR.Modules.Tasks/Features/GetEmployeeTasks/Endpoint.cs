using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed class Endpoint(GetEmployeeTasksHandler handler) : Endpoint<GetEmployeeTasksRequest, GetEmployeeTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/tasks");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(GetEmployeeTasksRequest request, CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await SendAsync(response, StatusCodes.Status200OK, cancellationToken);
    }
}
