using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RemoveMyEmergencyContact;

internal sealed class Endpoint(RemoveMyEmergencyContactHandler handler)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/me/emergency-contacts/{contactId:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
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
