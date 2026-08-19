using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed record HrSettingsAuditSnapshot(
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays,
    string DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    bool AutoDisableAccessOnLeavingDate,
    EmployeeNumberMode EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int NextEmployeeNumber,
    int EmployeeNumberMinimumLength);

internal sealed record HrSettingsUpdatedAuditEvent(
    Guid CompanyId,
    string? ActorId,
    DateTimeOffset OccurredAt,
    HrSettingsAuditSnapshot? PreviousSettings,
    HrSettingsAuditSnapshot CurrentSettings) : IAuditEvent
{
    string IAuditEvent.EventType => "hr-settings.updated";
    string IAuditEvent.EntityType => "CompanySettings";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "HR settings updated";
    object? IAuditEvent.Before => PreviousSettings;
    object? IAuditEvent.After => CurrentSettings;
    object? IAuditEvent.Metadata => null;
}
