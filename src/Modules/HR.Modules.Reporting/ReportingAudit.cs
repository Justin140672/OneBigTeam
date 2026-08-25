using HR.SharedKernel;

namespace HR.Modules.Reporting;

/// <summary>
/// Audit record for a single report export attempt (REP-06). Published after authorization has
/// already succeeded (endpoints/handlers reject unauthorized callers before any of this runs), so
/// this event only ever represents a successful or failed *generation* attempt — never an
/// authorization rejection, which is already covered separately by standard auth-failure logging.
///
/// Deliberately excludes exported report contents: <see cref="Filters"/> carries only structured
/// filter criteria (ids, dates, enum names) taken from the export request, never row data, employee
/// names or other PII from the generated report itself.
/// </summary>
internal sealed record ReportExportAuditEvent(
    Guid CompanyId,
    string ReportId,
    string Format,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string?> Filters,
    int? RowCount,
    bool Success,
    bool ManagerScopeApplied,
    string Sensitivity,
    string? FailureReason = null) : IAuditEvent
{
    Guid? IAuditEvent.ActorEmployeeId => null;

    string IAuditEvent.EventType => Success ? "report.exported" : "report.export-failed";

    string IAuditEvent.EntityType => "ReportExport";

    Guid IAuditEvent.EntityId => Guid.NewGuid();

    Guid? IAuditEvent.EmployeeId => null;

    Guid? IAuditEvent.CorrelationId => null;

    string? IAuditEvent.Summary => Success
        ? $"Report '{ReportId}' exported as {Format}" + (RowCount is { } rowCount ? $" ({rowCount} row(s))" : string.Empty)
        : $"Report '{ReportId}' export failed ({Format})" + (FailureReason is null ? string.Empty : $": {FailureReason}");

    object? IAuditEvent.Before => null;

    object? IAuditEvent.After => Success ? new { RowCount } : null;

    object? IAuditEvent.Metadata => new
    {
        ReportId,
        Format,
        Sensitivity,
        ManagerScopeApplied,
        Success,
        FailureReason,
        Filters,
    };
}
