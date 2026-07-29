using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;

internal sealed class Endpoint(ExportEmployeeDirectoryReportHandler handler)
    : Endpoint<ExportEmployeeDirectoryReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-directory/export");
        // Same PII justification as GetEmployeeDirectoryReport — exports must not widen access
        // beyond the report itself, so the same HR-only policy applies here.
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportEmployeeDirectoryReportRequest request,
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
