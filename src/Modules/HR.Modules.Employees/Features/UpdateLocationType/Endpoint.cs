using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateLocationType;

internal sealed class Endpoint(UpdateLocationTypeHandler handler)
    : Endpoint<UpdateLocationTypeRequest, UpdateLocationTypeResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/location-types/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(UpdateLocationTypeRequest request, CancellationToken cancellationToken)
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
