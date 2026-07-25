using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetTask;

internal sealed class Endpoint(GetTaskHandler handler) : Endpoint<GetTaskRequest, GetTaskResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
