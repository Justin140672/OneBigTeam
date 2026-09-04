using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyEqualityData;

internal sealed class Endpoint(GetMyEqualityDataHandler handler, ICurrentUser currentUser)
    : Endpoint<GetMyEqualityDataRequest, GetMyEqualityDataResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/equality-record");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMyEqualityDataRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } authenticatedEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (authenticatedEmployeeId != request.EmployeeId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
