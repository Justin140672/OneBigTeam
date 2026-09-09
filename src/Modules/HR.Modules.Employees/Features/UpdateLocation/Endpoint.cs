using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateLocation;

internal sealed class Endpoint(
    UpdateLocationHandler handler) : Endpoint<UpdateLocationRequest, UpdateLocationResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/locations/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdateLocationRequest request,
        CancellationToken cancellationToken)
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
