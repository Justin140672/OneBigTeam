using FastEndpoints;

namespace HR.Modules.Reporting.Features.ExportSicknessReport;

internal sealed class Endpoint(ExportSicknessReportHandler handler)
    : Endpoint<ExportSicknessReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/sickness/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportSicknessReportRequest request,
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
