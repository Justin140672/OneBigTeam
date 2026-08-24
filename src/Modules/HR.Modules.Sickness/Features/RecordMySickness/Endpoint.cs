using FastEndpoints;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.RecordMySickness;

internal sealed class Endpoint(RecordMySicknessHandler handler, ICurrentUser currentUser)
    : Endpoint<RecordMySicknessRequest, RecordSicknessResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records/my");
        Policies("role:employee");
    }

    public override async Task HandleAsync(RecordMySicknessRequest request, CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } authenticatedEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (authenticatedEmployeeId != request.EmployeeId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = authenticatedEmployeeId },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/employees/{request.EmployeeId}/sickness-records/{result.Value!.Id}",
            result.Value));
    }
}
