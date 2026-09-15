using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetDirectoryEmployee;

internal sealed class Endpoint(
    GetDirectoryEmployeeHandler handler) : Endpoint<GetDirectoryEmployeeRequest, GetDirectoryEmployeeResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/directory/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetDirectoryEmployeeRequest request,
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
