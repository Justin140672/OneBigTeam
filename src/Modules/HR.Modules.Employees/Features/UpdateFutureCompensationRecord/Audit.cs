using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateFutureCompensationRecord;

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
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record updated";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Salary, Currency };
    object? IAuditEvent.Metadata => null;
}
