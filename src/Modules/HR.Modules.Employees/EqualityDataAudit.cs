using HR.SharedKernel;

namespace HR.Modules.Employees;

/// <summary>
/// Voluntary equality monitoring data is special-category personal data. Audit payloads therefore
/// carry NO answer values — only ids, timestamps and boolean "was X provided" flags. Self-service
/// only: the subject and the actor are the same person.
/// </summary>
internal sealed record EqualityDataUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid RecordId,
    bool Created,
    bool GenderIdentityProvided,
    bool MarriedOrCivilPartnershipStatusProvided,
    bool EthnicGroupProvided,
    bool DisabilityStatusProvided,
    bool SexualOrientationProvided,
    bool ReligionOrBeliefProvided,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.equality_data.updated";
    string IAuditEvent.EntityType => "EmployeeEqualityData";
    Guid IAuditEvent.EntityId => RecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => Created ? "Equality monitoring data provided" : "Equality monitoring data updated";
    object? IAuditEvent.Before => null;
    // Deliberately NO answer values — only presence flags.
    object? IAuditEvent.After => new
    {
        Created,
        GenderIdentityProvided,
        MarriedOrCivilPartnershipStatusProvided,
        EthnicGroupProvided,
        DisabilityStatusProvided,
        SexualOrientationProvided,
        ReligionOrBeliefProvided
    };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EqualityDataDeletedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid RecordId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.equality_data.deleted";
    string IAuditEvent.EntityType => "EmployeeEqualityData";
    Guid IAuditEvent.EntityId => RecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Equality monitoring data withdrawn";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
