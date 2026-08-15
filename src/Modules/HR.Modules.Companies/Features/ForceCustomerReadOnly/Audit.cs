using HR.SharedKernel;

namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

/// <summary>
/// Records a platform-administrator forcing a company into read-only mode (e.g. suspected abuse or
/// a billing dispute), independent of trial/subscription status. Uses the same cross-cutting
/// IAuditEventPublisher as every other audited action in this module (see ExtendCustomerTrial's
/// Audit.cs remarks).
/// </summary>
internal sealed record ReadOnlyModeForcedByAdminAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "subscription.admin-forced-read-only";
    string IAuditEvent.EntityType => "CustomerSubscription";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Read-only mode forced by platform administrator. Reason: {Reason}";
    object? IAuditEvent.Before => new { AdminForcedReadOnly = false };
    object? IAuditEvent.After => new { AdminForcedReadOnly = true };
    object? IAuditEvent.Metadata => new { Reason };
}
