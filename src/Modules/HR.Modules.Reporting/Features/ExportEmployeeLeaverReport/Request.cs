using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;

internal sealed record ExportEmployeeLeaverReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    string? SortBy = null,
    bool SortDescending = false,
    ReportExportFormat Format = ReportExportFormat.Csv);
