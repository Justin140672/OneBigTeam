using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateMyContactDetails;

internal sealed class Endpoint(
    UpdateMyContactDetailsHandler handler) : Endpoint<UpdateMyContactDetailsRequest, UpdateMyContactDetailsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/me/contact-details");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        UpdateMyContactDetailsRequest request,
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
