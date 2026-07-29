using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;

internal sealed record ExportRecruitmentPipelineReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    RecruitmentPipelineGroupBy GroupBy = RecruitmentPipelineGroupBy.Recruiter,
    ReportExportFormat Format = ReportExportFormat.Csv);
