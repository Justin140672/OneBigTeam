using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetDepartment;

internal sealed class Endpoint(
    GetDepartmentHandler handler) : Endpoint<GetDepartmentRequest, GetDepartmentResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/departments/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
