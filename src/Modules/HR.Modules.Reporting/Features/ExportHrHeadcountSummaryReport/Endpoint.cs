using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportHrHeadcountSummaryReport;

internal sealed class Endpoint(ExportHrHeadcountSummaryReportHandler handler)
    : Endpoint<ExportHrHeadcountSummaryReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/hr-headcount-summary/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportHrHeadcountSummaryReportRequest request,
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
