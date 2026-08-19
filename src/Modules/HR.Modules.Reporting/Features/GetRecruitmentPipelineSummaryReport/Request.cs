namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

internal sealed record GetRecruitmentPipelineSummaryReportRequest(
    Guid CompanyId,
    bool IncludeClosed = false);
