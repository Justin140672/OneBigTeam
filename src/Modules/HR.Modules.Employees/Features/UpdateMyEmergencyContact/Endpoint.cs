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
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
