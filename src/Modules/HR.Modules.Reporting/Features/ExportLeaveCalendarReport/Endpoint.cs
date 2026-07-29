using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportLeaveCalendarReport;

internal sealed class Endpoint(ExportLeaveCalendarReportHandler handler)
    : Endpoint<ExportLeaveCalendarReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/leave-calendar/export");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        ExportLeaveCalendarReportRequest request,
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
