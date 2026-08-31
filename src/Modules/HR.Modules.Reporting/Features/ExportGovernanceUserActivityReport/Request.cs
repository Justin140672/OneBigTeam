using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;

internal sealed record ExportGovernanceUserActivityReportRequest(
    Guid CompanyId,
    Guid? ActorUserId = null,
    string? EventType = null,
    Guid? EmployeeId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    ReportExportFormat Format = ReportExportFormat.Csv);
