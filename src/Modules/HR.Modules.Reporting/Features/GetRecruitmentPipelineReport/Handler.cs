using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

internal sealed class GetRecruitmentPipelineReportHandler(IRecruitmentPipelineReader recruitmentPipelineReader)
{
    public async Task<Result<GetRecruitmentPipelineReportResponse>> HandleAsync(
        GetRecruitmentPipelineReportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GroupBy == RecruitmentPipelineGroupBy.Vacancy)
        {
            var vacancyRows = await recruitmentPipelineReader.GetByVacancyAsync(
                request.CompanyId, request.StartDate, request.EndDate, cancellationToken);

            var items = vacancyRows
                .Select(r => new RecruitmentPipelineReportRow(
                    r.VacancyId.ToString(), r.VacancyTitle, 1, r.Applicants, r.Interviews, r.Offers, r.Hires))
                .ToList();

            return Result.Success(new GetRecruitmentPipelineReportResponse(items));
        }

        var recruiterRows = await recruitmentPipelineReader.GetByRecruiterAsync(
            request.CompanyId, request.StartDate, request.EndDate, cancellationToken);

        var recruiterItems = recruiterRows
            .Select(r => new RecruitmentPipelineReportRow(
                r.RecruiterId?.ToString() ?? "unassigned", r.RecruiterName, r.Vacancies, r.Applicants, r.Interviews, r.Offers, r.Hires))
            .ToList();

        return Result.Success(new GetRecruitmentPipelineReportResponse(recruiterItems));
    }
}
