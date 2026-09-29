using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyEmergencyContacts;

internal sealed class Endpoint(GetMyEmergencyContactsHandler handler, ICurrentUser currentUser)
    : EndpointWithoutRequest<GetMyEmergencyContactsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/emergency-contacts");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyId = Route<Guid>("companyId");

        var result = await handler.HandleAsync(companyId, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
