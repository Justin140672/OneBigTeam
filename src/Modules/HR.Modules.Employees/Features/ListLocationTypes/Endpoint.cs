using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListLocationTypes;

internal sealed class Endpoint(ListLocationTypesHandler handler)
    : Endpoint<ListLocationTypesRequest, ListLocationTypesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/location-types");
        Policies("role:employee");
    }

    public override async Task HandleAsync(ListLocationTypesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
