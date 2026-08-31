using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;

internal sealed class Endpoint(GetGovernanceAdministrativeChangesReportHandler handler)
    : Endpoint<GetGovernanceAdministrativeChangesReportRequest, GetGovernanceAdministrativeChangesReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/administrative-changes");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        GetGovernanceAdministrativeChangesReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
