using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.CreateTask;

internal sealed class Endpoint(CreateTaskHandler handler) : Endpoint<CreateTaskRequest, CreateTaskResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/tasks");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var createdBy))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request with { CreatedBy = createdBy }, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/tasks/{result.Value.Id}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
