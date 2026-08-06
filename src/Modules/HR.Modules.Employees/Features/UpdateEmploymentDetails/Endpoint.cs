using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed class Endpoint(
    UpdateEmploymentDetailsHandler handler, ICurrentUser currentUser) : Endpoint<UpdateEmploymentDetailsRequest, UpdateEmploymentDetailsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{id:guid}/employment");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdateEmploymentDetailsRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
