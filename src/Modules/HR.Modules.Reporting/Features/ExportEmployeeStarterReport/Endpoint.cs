using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportEmployeeStarterReport;

internal sealed class Endpoint(ExportEmployeeStarterReportHandler handler)
    : Endpoint<ExportEmployeeStarterReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-starters/export");
        Policies("reporting:view-employee-starter");
    }

    public override async Task HandleAsync(
        ExportEmployeeStarterReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        var file = result.Value!.File;

        await Send.BytesAsync(
            file.Content,
            fileName: file.FileName,
            contentType: file.ContentType,
            cancellation: cancellationToken);
    }
}
