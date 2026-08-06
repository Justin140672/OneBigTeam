using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed class Endpoint(GetMyTasksHandler handler, ICurrentUser currentUser) : Endpoint<GetMyTasksRequest, GetMyTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/my");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMyTasksRequest request, CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } userId)
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
