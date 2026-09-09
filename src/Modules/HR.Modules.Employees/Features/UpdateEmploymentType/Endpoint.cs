using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateEmploymentType;

internal sealed class Endpoint(UpdateEmploymentTypeHandler handler)
    : Endpoint<UpdateEmploymentTypeRequest, UpdateEmploymentTypeResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employment-types/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(UpdateEmploymentTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
