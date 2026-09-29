using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;

internal sealed class Endpoint(GetHrHeadcountSummaryReportHandler handler)
    : Endpoint<GetHrHeadcountSummaryReportRequest, GetHrHeadcountSummaryReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/hr-headcount-summary");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetHrHeadcountSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
