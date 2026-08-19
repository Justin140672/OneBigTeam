using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

internal sealed class Endpoint(GetRecruitmentPipelineSummaryReportHandler handler)
    : Endpoint<GetRecruitmentPipelineSummaryReportRequest, GetRecruitmentPipelineSummaryReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/recruitment-pipeline-summary");
        Policies("reporting:view-recruitment");
    }

    public override async Task HandleAsync(
        GetRecruitmentPipelineSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
