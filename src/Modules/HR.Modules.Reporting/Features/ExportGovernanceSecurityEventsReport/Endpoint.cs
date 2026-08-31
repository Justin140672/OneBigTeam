using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;

internal sealed class Endpoint(ExportGovernanceSecurityEventsReportHandler handler)
    : Endpoint<ExportGovernanceSecurityEventsReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/security-events/export");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        ExportGovernanceSecurityEventsReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        var file = result.Value!.File;
        await Send.BytesAsync(file.Content, fileName: file.FileName, contentType: file.ContentType, cancellation: cancellationToken);
    }
}
