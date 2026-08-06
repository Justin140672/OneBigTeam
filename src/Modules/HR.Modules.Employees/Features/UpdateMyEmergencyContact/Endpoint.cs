using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

internal sealed class Endpoint(UpdateMyEmergencyContactHandler handler, ICurrentUser currentUser)
    : Endpoint<UpdateMyEmergencyContactRequest, UpdateMyEmergencyContactResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/me/emergency-contacts/{contactId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        UpdateMyEmergencyContactRequest request,
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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
