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
