using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetTask;

internal sealed class Endpoint(GetTaskHandler handler) : Endpoint<GetTaskRequest, GetTaskResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/{id:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(GetTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
