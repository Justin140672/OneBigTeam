using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;

internal sealed class Endpoint(GetEmployeeDirectoryReportHandler handler)
    : Endpoint<GetEmployeeDirectoryReportRequest, GetEmployeeDirectoryReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-directory");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetEmployeeDirectoryReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
