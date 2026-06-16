using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyPersonalDetails;

internal sealed class Endpoint(GetMyPersonalDetailsHandler handler) : EndpointWithoutRequest<GetMyPersonalDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/personal-details");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await SendResultAsync(TypedResults.BadRequest());
            return;
        }

        var result = await handler.HandleAsync(companyId, userId, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound());
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
