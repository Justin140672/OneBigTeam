using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

internal sealed class Endpoint(UpdateMyEmergencyContactHandler handler)
    : Endpoint<UpdateMyEmergencyContactRequest, UpdateMyEmergencyContactResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/me/emergency-contacts/{contactId:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        UpdateMyEmergencyContactRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound());
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
