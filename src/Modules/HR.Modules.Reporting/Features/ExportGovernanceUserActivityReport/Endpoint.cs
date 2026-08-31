using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;

internal sealed class Endpoint(ExportGovernanceUserActivityReportHandler handler)
    : Endpoint<ExportGovernanceUserActivityReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/user-activity/export");
        // Identical policy pair to GetGovernanceUserActivityReport — exports must not widen access.
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        ExportGovernanceUserActivityReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        var file = result.Value!.File;
        await Send.BytesAsync(file.Content, fileName: file.FileName, contentType: file.ContentType, cancellation: cancellationToken);
    }
}
