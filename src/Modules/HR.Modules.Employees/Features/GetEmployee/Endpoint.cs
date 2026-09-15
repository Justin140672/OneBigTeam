using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed class Endpoint(
    GetEmployeeHandler handler) : Endpoint<GetEmployeeRequest, GetEmployeeResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetEmployeeRequest request,
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
