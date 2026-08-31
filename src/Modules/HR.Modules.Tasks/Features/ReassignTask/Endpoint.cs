using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed class Endpoint(ReassignTaskHandler handler, ICurrentUser currentUser) : Endpoint<ReassignTaskRequest, ReassignTaskResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/tasks/{id:guid}/assignee");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ReassignTaskRequest request, CancellationToken cancellationToken)
    {
        // DSH-01: derive the acting identity from the authenticated principal.
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorUserId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            request with { ActorUserId = actorUserId },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "forbidden")
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }

            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
