using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyEmergencyContacts;

internal sealed class Endpoint(GetMyEmergencyContactsHandler handler)
    : EndpointWithoutRequest<GetMyEmergencyContactsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/emergency-contacts");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound());
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
