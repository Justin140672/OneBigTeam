using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetProposedLastWorkingDay;

internal sealed class Endpoint(GetProposedLastWorkingDayHandler handler)
    : Endpoint<GetProposedLastWorkingDayRequest, GetProposedLastWorkingDayResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/proposed-last-working-day");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetProposedLastWorkingDayRequest request,
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
