namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

internal sealed record GetRecruitmentPipelineReportResponse(IReadOnlyList<RecruitmentPipelineReportRow> Items);

internal sealed record RecruitmentPipelineReportRow(
    string GroupKey,
    string GroupLabel,
    int Vacancies,
    int Candidates,
    int Interviews,
    int Offers,
    int Hires);
