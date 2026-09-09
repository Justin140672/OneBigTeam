using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateDepartment;

internal sealed class Endpoint(
    UpdateDepartmentHandler handler) : Endpoint<UpdateDepartmentRequest, UpdateDepartmentResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/departments/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdateDepartmentRequest request,
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
