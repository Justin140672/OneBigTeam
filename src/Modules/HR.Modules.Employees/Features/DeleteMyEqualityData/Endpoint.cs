using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.DeleteMyEqualityData;

internal sealed class Endpoint(DeleteMyEqualityDataHandler handler, ICurrentUser currentUser)
    : Endpoint<DeleteMyEqualityDataRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/{employeeId:guid}/equality-record");
        Policies("role:employee");
    }

    public override async Task HandleAsync(DeleteMyEqualityDataRequest request, CancellationToken cancellationToken)
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
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.NoContent());
    }
}
