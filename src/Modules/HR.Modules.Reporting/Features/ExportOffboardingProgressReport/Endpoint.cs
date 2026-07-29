using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportOffboardingProgressReport;

internal sealed class Endpoint(ExportOffboardingProgressReportHandler handler)
    : Endpoint<ExportOffboardingProgressReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/offboarding-progress/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportOffboardingProgressReportRequest request,
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
