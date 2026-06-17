using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed class Endpoint(AddMyEmergencyContactHandler handler)
    : Endpoint<AddMyEmergencyContactRequest, AddMyEmergencyContactResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/me/emergency-contacts");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        AddMyEmergencyContactRequest request,
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

        await SendAsync(result.Value!, StatusCodes.Status201Created, cancellationToken);
    }
}
