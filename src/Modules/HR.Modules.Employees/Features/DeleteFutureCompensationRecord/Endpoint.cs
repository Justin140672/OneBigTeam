using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.DeleteFutureCompensationRecord;

internal sealed class Endpoint(DeleteFutureCompensationRecordHandler handler, ICurrentUser currentUser) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/{employeeId:guid}/compensation/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId  = Route<Guid>("companyId");
        var employeeId = Route<Guid>("employeeId");
        var id         = Route<Guid>("id");

        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(companyId, employeeId, id, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
