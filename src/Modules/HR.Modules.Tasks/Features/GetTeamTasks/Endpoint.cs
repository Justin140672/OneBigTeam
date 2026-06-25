using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed class Endpoint(GetTeamTasksHandler handler) : Endpoint<GetTeamTasksRequest, GetTeamTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-tasks");
        Policies("authenticated");
    }

    public override async Task HandleAsync(GetTeamTasksRequest request, CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
