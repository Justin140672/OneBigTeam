using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetSicknessReport;

namespace HR.Modules.Reporting.Features.ExportSicknessReport;

internal sealed record ExportSicknessReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    SicknessReportGroupBy GroupBy = SicknessReportGroupBy.Employee,
    ReportExportFormat Format = ReportExportFormat.Csv);
