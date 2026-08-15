using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ExtendCustomerTrial;

internal sealed record TrialExtendedAuditSnapshot(string Status, DateTimeOffset TrialExpiresAt);

/// <summary>
/// Records a platform-administrator trial extension (Subscription Management epic). Plugs into
/// the existing cross-cutting IAuditEventPublisher/AuditDbContext infrastructure already used by
/// every other module (see e.g. HR.Modules.Companies.Features.UpdateHrSettings.Audit) rather than
/// a new module-local audit table — deliberately reusing the platform's one audit mechanism.
/// </summary>
internal sealed record TrialExtendedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason,
    TrialExtendedAuditSnapshot? PreviousState,
    TrialExtendedAuditSnapshot CurrentState) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.trial-extended";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Trial extended by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => new { Reason };
}
