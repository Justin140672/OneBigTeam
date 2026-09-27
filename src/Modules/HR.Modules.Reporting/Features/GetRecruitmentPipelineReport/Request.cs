namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

internal enum RecruitmentPipelineGroupBy
{
    Recruiter = 1,
    Vacancy = 2,
}

internal sealed record GetRecruitmentPipelineReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    RecruitmentPipelineGroupBy GroupBy = RecruitmentPipelineGroupBy.Recruiter,
    // Internal recruitment Ticket 6: null = all applications, true = internal only, false = external only.
    bool? IsInternal = null);
