using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;

internal sealed class Endpoint(ListEmployeeSicknessRecordsHandler handler)
    : Endpoint<ListEmployeeSicknessRecordsRequest, ListEmployeeSicknessRecordsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ListEmployeeSicknessRecordsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
