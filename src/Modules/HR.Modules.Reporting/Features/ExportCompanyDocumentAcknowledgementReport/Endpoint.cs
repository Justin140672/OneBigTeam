using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;

internal sealed class Endpoint(ExportCompanyDocumentAcknowledgementReportHandler handler)
    : Endpoint<ExportCompanyDocumentAcknowledgementReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/document-acknowledgement/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportCompanyDocumentAcknowledgementReportRequest request,
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
