using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed class Endpoint(GetMyTasksHandler handler) : Endpoint<GetMyTasksRequest, GetMyTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/my");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMyTasksRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var response = await handler.HandleAsync(
            request with { UserId = userId },
            cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
