using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

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
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Emergency contact updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}
