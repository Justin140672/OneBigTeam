using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateMyContactDetails;

internal sealed class Endpoint(
    UpdateMyContactDetailsHandler handler, ICurrentUser currentUser) : Endpoint<UpdateMyContactDetailsRequest, UpdateMyContactDetailsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/me/contact-details");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        UpdateMyContactDetailsRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
