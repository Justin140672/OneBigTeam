using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListLocations;

internal sealed class Endpoint(
    ListLocationsHandler handler) : Endpoint<ListLocationsRequest, ListLocationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/locations");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListLocationsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
