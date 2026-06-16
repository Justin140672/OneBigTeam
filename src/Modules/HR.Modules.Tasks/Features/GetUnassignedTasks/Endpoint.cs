using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetUnassignedTasks;

internal sealed class Endpoint(GetUnassignedTasksHandler handler)
    : Endpoint<GetUnassignedTasksRequest, GetUnassignedTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/unassigned");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(GetUnassignedTasksRequest request, CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await SendAsync(response, StatusCodes.Status200OK, cancellationToken);
    }
}
