using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees;

internal sealed record CompensationRecordCreatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.created";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Salary, Currency };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordClosedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.closed";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record closed";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { EffectiveFrom, EffectiveTo };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.updated";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record updated";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Salary, Currency };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordDeletedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.deleted";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record deleted";
    object? IAuditEvent.Before => new { EffectiveFrom };
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordReopenedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    DateOnly PreviousEffectiveTo,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.reopened";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record reopened after deletion of its successor";
    object? IAuditEvent.Before => new { EffectiveTo = PreviousEffectiveTo };
    object? IAuditEvent.After => new { EffectiveTo = (DateOnly?)null };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmployeeCreatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Source,
    Guid? ImportSessionId) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.created";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => ImportSessionId;
    string? IAuditEvent.Summary => Source == "Import" ? "Employee created via import" : "Employee created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Source, ImportSessionId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmergencyContactAddedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    Guid ContactId,
    string Name,
    string Relationship) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.emergency-contact.added";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Emergency contact added";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ContactId, Name, Relationship };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmergencyContactSnapshot(
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

internal sealed record EmergencyContactUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    EmergencyContactSnapshot? Before,
    EmergencyContactSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.emergency-contact.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Emergency contact updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmployeeProfileSnapshot(
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    string? PreferredName,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    Guid? LocationId,
    bool HasSystemAccess);

internal sealed record EmployeeProfileUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    EmployeeProfileSnapshot Before,
    EmployeeProfileSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.profile.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee profile updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record ContactDetailsSnapshot(
    string? PersonalEmail,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country);

internal sealed record ContactDetailsUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    ContactDetailsSnapshot? Before,
    ContactDetailsSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.contact-details.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee contact details updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record PositionProfileSnapshot(
    Guid DepartmentId,
    Guid LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    SalaryType? SalaryType,
    Guid DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    bool IsActive);

internal sealed record PositionProfileCreatedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    PositionProfileSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.created";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Position profile created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record PositionProfileUpdatedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    PositionProfileSnapshot Before,
    PositionProfileSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.updated";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Position profile updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeavingProcessSnapshot(
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    NoticePeriodSource NoticeSource,
    LeavingReason LeavingReason,
    LeavingProcessStatus Status);

internal sealed record LeavingProcessStartedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    LeavingProcessSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.leaving-process.started";
    string IAuditEvent.EntityType => "EmployeeLeavingProcess";
    Guid IAuditEvent.EntityId => LeavingProcessId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leaving process started";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => new { EmployeeStatusChangedTo = "Leaving" };
}

internal sealed record LeavingProcessAmendedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    LeavingProcessSnapshot Before,
    LeavingProcessSnapshot After,
    bool OffboardingAlreadyStarted) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.leaving-process.amended";
    string IAuditEvent.EntityType => "EmployeeLeavingProcess";
    Guid IAuditEvent.EntityId => LeavingProcessId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leaving process amended";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => new { OffboardingAlreadyStarted };
}

internal sealed record LeavingProcessCancelledAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    string CancellationReason,
    bool OffboardingTasksCancelled) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.leaving-process.cancelled";
    string IAuditEvent.EntityType => "EmployeeLeavingProcess";
    Guid IAuditEvent.EntityId => LeavingProcessId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leaving process cancelled";
    object? IAuditEvent.Before => new { Status = "InProgress" };
    object? IAuditEvent.After => new { Status = "Cancelled", CancellationReason };
    object? IAuditEvent.Metadata => new { EmployeeStatusChangedTo = "Active", OffboardingTasksCancelled };
}

// Published by ProcessLeavingEmployeesJob (Hangfire) once an employee's leaving date has passed
// and their departure has been finalised. There is no ActorUserId/ActorEmployeeId — this is a
// system-driven transition, not a user action — mirroring how other unattended-job audit events
// in this codebase represent the system as the actor (null).
internal sealed record EmployeeDepartureFinalisedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    DateTimeOffset OccurredAt,
    bool AccessDisabled,
    bool OffboardingIncomplete) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.departure.finalised";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee departure finalised";
    object? IAuditEvent.Before => new { Status = "Leaving" };
    object? IAuditEvent.After => new { Status = "FormerEmployee" };
    object? IAuditEvent.Metadata => new { AccessDisabled, OffboardingIncomplete };
}
