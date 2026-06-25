using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListNationalities;

internal sealed class Endpoint(ListNationalitiesHandler handler)
    : EndpointWithoutRequest<ListNationalitiesResponse>
{
    public override void Configure()
    {
        Get("/api/nationalities");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
