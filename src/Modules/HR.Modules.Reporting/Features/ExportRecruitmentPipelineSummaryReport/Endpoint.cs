using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;

internal sealed class Endpoint(ExportRecruitmentPipelineSummaryReportHandler handler)
    : Endpoint<ExportRecruitmentPipelineSummaryReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/recruitment-pipeline-summary/export");
        Policies("reporting:view-recruitment");
    }

    public override async Task HandleAsync(
        ExportRecruitmentPipelineSummaryReportRequest request,
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
