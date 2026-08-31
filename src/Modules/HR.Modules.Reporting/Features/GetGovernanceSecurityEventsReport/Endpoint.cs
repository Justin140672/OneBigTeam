using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;

internal sealed class Endpoint(GetGovernanceSecurityEventsReportHandler handler)
    : Endpoint<GetGovernanceSecurityEventsReportRequest, GetGovernanceSecurityEventsReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/governance/security-events");
        Policies("reporting:view", "reporting:view-governance");
    }

    public override async Task HandleAsync(
        GetGovernanceSecurityEventsReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
