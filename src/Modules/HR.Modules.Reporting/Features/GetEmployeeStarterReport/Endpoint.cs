using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetEmployeeStarterReport;

internal sealed class Endpoint(GetEmployeeStarterReportHandler handler)
    : Endpoint<GetEmployeeStarterReportRequest, GetEmployeeStarterReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-starters");
        Policies("reporting:view-employee-starter");
    }

    public override async Task HandleAsync(
        GetEmployeeStarterReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
