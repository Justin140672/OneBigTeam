using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateLocationType;

internal sealed class Endpoint(CreateLocationTypeHandler handler)
    : Endpoint<CreateLocationTypeRequest, CreateLocationTypeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/location-types");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CreateLocationTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Created($"/api/companies/{request.CompanyId}/location-types/{result.Value!.Id}", result.Value));
    }
}
