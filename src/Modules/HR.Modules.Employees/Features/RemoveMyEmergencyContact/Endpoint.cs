using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RemoveMyEmergencyContact;

internal sealed class Endpoint(RemoveMyEmergencyContactHandler handler, ICurrentUser currentUser)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/me/emergency-contacts/{contactId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyId = Route<Guid>("companyId");
        var contactId = Route<Guid>("contactId");

        var result = await handler.HandleAsync(companyId, employeeId, contactId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
