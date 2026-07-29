using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportWorkloadActions;

internal sealed record ExportWorkloadActionsRequest(
    Guid CompanyId,
    string? ActionType = null,
    string? Department = null,
    string? Urgency = null,
    string? Status = null,
    Guid? EmployeeId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    string? GroupBy = null,
    Guid? ManagerId = null,
    Guid? LocationId = null,
    string? RecruitmentUser = null,
    ReportExportFormat Format = ReportExportFormat.Csv);
