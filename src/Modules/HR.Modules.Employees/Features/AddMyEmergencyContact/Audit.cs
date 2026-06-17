using HR.SharedKernel;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

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
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Emergency contact added";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ContactId, Name, Relationship };
    object? IAuditEvent.Metadata => null;
}
