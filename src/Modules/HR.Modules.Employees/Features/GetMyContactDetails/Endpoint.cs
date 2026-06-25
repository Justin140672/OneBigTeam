using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyContactDetails;

internal sealed class Endpoint(GetMyContactDetailsHandler handler) : EndpointWithoutRequest<GetMyContactDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/contact-details");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        var result = await handler.HandleAsync(companyId, userId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
