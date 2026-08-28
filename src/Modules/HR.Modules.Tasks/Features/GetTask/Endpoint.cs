using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetTask;

internal sealed class Endpoint(GetTaskHandler handler, ICurrentUser currentUser) : Endpoint<GetTaskRequest, GetTaskResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetTaskRequest request, CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
        // resolved Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            request with { CallerEmployeeId = callerEmployeeId },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            if (result.Error.Code == "forbidden")
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
