using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

internal sealed record GetRecruitmentPipelineSummaryReportResponse(
    IReadOnlyList<RecruitmentPipelineSummaryRow> Vacancies,
    IReadOnlyList<RecruitmentStageColumn> Stages);
