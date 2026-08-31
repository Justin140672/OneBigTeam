using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;

internal sealed record ExportGovernanceComplianceStatusReportRequest(
    Guid CompanyId,
    string? Category = null,
    string? Severity = null,
    string? Department = null,
    Guid? ManagerId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    ReportExportFormat Format = ReportExportFormat.Csv);
