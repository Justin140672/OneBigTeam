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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
