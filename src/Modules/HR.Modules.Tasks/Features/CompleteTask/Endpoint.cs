using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed class Endpoint(CompleteTaskHandler handler) : Endpoint<CompleteTaskRequest, CompleteTaskResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/tasks/{id:guid}/complete");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CompleteTaskRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var completedBy))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            request with { CompletedBy = completedBy },
            cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await SendResultAsync(TypedResults.Conflict(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
