using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportGovernanceAdministrativeChangesReport;

internal sealed class Endpoint(ExportGovernanceAdministrativeChangesReportHandler handler)
    : Endpoint<ExportGovernanceAdministrativeChangesReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/administrative-changes/export");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        ExportGovernanceAdministrativeChangesReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        var file = result.Value!.File;
        await Send.BytesAsync(file.Content, fileName: file.FileName, contentType: file.ContentType, cancellation: cancellationToken);
    }
}
