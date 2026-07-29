using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;

internal sealed class Endpoint(ExportEmployeeLeaverReportHandler handler)
    : Endpoint<ExportEmployeeLeaverReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-leavers/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportEmployeeLeaverReportRequest request,
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
