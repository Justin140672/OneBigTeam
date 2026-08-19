using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

internal sealed class GetRecruitmentPipelineSummaryReportHandler(IRecruitmentPipelineSummaryReader recruitmentPipelineSummaryReader)
{
    public async Task<Result<GetRecruitmentPipelineSummaryReportResponse>> HandleAsync(
        GetRecruitmentPipelineSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await recruitmentPipelineSummaryReader.GetSummaryAsync(
            request.CompanyId, request.IncludeClosed, cancellationToken);

        return Result.Success(new GetRecruitmentPipelineSummaryReportResponse(result.Vacancies, result.Stages));
    }
}
