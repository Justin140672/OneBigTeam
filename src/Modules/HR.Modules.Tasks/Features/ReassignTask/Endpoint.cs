using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed class Endpoint(ReassignTaskHandler handler) : Endpoint<ReassignTaskRequest, ReassignTaskResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/tasks/{id:guid}/assignee");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ReassignTaskRequest request, CancellationToken cancellationToken)
    {
        Guid.TryParse(User.FindFirst("sub")?.Value, out var actorUserId);

        var result = await handler.HandleAsync(
            request with { ActorUserId = actorUserId == Guid.Empty ? null : actorUserId },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
