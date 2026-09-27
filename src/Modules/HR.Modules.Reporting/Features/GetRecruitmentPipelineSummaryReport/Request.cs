namespace HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

internal sealed record GetRecruitmentPipelineSummaryReportRequest(
    Guid CompanyId,
    bool IncludeClosed = false,
    // Internal recruitment Ticket 6: null = all applications, true = internal only, false = external only.
    bool? IsInternal = null);
