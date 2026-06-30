using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateEmploymentType;

internal sealed class Endpoint(CreateEmploymentTypeHandler handler)
    : Endpoint<CreateEmploymentTypeRequest, CreateEmploymentTypeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employment-types");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CreateEmploymentTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Created($"/api/companies/{request.CompanyId}/employment-types/{result.Value!.Id}", result.Value));
    }
}
