using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEmployeePromotionHistory;

internal sealed class Endpoint(GetEmployeePromotionHistoryHandler handler)
    : EndpointWithoutRequest<GetEmployeePromotionHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/promotions");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId  = Route<Guid>("companyId");
        var employeeId = Route<Guid>("employeeId");

        var result = await handler.HandleAsync(companyId, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
