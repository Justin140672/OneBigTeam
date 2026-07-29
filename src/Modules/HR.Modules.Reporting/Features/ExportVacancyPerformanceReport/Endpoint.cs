using FastEndpoints;

namespace HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;

internal sealed class Endpoint(ExportVacancyPerformanceReportHandler handler)
    : Endpoint<ExportVacancyPerformanceReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/vacancy-performance/export");
        Policies("reporting:view-recruitment");
    }

    public override async Task HandleAsync(
        ExportVacancyPerformanceReportRequest request,
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
