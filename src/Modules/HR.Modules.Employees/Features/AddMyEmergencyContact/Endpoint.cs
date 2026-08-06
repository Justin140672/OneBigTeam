using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed class Endpoint(AddMyEmergencyContactHandler handler, ICurrentUser currentUser)
    : Endpoint<AddMyEmergencyContactRequest, AddMyEmergencyContactResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/me/emergency-contacts");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        AddMyEmergencyContactRequest request,
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

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
