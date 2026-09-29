using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetMySicknessRecords;

internal sealed class Endpoint(GetMySicknessRecordsHandler handler, ICurrentUser currentUser)
    : Endpoint<GetMySicknessRecordsRequest, GetMySicknessRecordsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records/my");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMySicknessRecordsRequest request, CancellationToken cancellationToken)
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
