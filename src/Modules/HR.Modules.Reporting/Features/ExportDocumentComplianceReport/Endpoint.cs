using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportDocumentComplianceReport;

internal sealed class Endpoint(ExportDocumentComplianceReportHandler handler)
    : Endpoint<ExportDocumentComplianceReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/document-compliance/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportDocumentComplianceReportRequest request,
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
