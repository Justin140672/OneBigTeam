using FastEndpoints;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;

internal sealed class Endpoint(ExportRecruitmentPipelineReportHandler handler)
    : Endpoint<ExportRecruitmentPipelineReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/recruitment-pipeline/export");
        Policies("reporting:view-recruitment");
    }

    public override async Task HandleAsync(
        ExportRecruitmentPipelineReportRequest request,
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
