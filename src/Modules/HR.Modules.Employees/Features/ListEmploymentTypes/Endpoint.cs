using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListEmploymentTypes;

internal sealed class Endpoint(ListEmploymentTypesHandler handler)
    : Endpoint<ListEmploymentTypesRequest, ListEmploymentTypesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employment-types");
        Policies("role:employee");
    }

    public override async Task HandleAsync(ListEmploymentTypesRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
