using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;

internal sealed class Endpoint(ExportGovernanceComplianceStatusReportHandler handler)
    : Endpoint<ExportGovernanceComplianceStatusReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/compliance-status/export");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        ExportGovernanceComplianceStatusReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        var file = result.Value!.File;
        await Send.BytesAsync(file.Content, fileName: file.FileName, contentType: file.ContentType, cancellation: cancellationToken);
    }
}
