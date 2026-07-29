using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

internal sealed class Endpoint(GetRecruitmentPipelineReportHandler handler)
    : Endpoint<GetRecruitmentPipelineReportRequest, GetRecruitmentPipelineReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/recruitment-pipeline");
        Policies("reporting:view-recruitment");
    }

    public override async Task HandleAsync(
        GetRecruitmentPipelineReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
