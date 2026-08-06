using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RequestPersonalDetailsChange;

internal sealed class Endpoint(RequestPersonalDetailsChangeHandler handler, ICurrentUser currentUser)
    : Endpoint<RequestPersonalDetailsChangeRequest, RequestPersonalDetailsChangeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/personal-details-change-requests");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        RequestPersonalDetailsChangeRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "forbidden")
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
