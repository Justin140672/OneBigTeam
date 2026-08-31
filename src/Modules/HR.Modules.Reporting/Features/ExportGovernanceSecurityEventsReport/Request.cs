using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;

internal sealed record ExportGovernanceSecurityEventsReportRequest(
    Guid CompanyId,
    Guid? ActorUserId = null,
    string? EventType = null,
    Guid? EmployeeId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    ReportExportFormat Format = ReportExportFormat.Csv);
