using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportAssetAssignmentReport;

internal sealed class Endpoint(ExportAssetAssignmentReportHandler handler)
    : Endpoint<ExportAssetAssignmentReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/asset-assignment/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportAssetAssignmentReportRequest request,
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
