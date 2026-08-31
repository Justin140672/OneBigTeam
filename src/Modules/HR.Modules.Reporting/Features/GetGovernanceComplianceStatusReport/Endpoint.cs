using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;

internal sealed class Endpoint(GetGovernanceComplianceStatusReportHandler handler)
    : Endpoint<GetGovernanceComplianceStatusReportRequest, GetGovernanceComplianceStatusReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/compliance-status");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        GetGovernanceComplianceStatusReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
