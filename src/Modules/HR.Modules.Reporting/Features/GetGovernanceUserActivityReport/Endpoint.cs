using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;

/// <summary>
/// ADM-08. Requires BOTH baseline reporting access and the elevated governance gate — the
/// "explicit reporting AND security permissions" the ticket mandates. The export endpoint applies
/// the identical policy pair so exported data can never have a wider scope than the on-screen report.
/// </summary>
internal sealed class Endpoint(GetGovernanceUserActivityReportHandler handler)
    : Endpoint<GetGovernanceUserActivityReportRequest, GetGovernanceUserActivityReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/user-activity");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        GetGovernanceUserActivityReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
