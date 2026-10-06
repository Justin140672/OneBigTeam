using HR.SharedKernel;

namespace HR.Modules.Companies.Features.RevokeSupportSession;

internal sealed record SupportSessionRevokedAuditEvent(
    Guid CompanyId,
    Guid SupportSessionId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "support.session-revoked";
    string IAuditEvent.EntityType => "SupportSession";
    Guid IAuditEvent.EntityId => SupportSessionId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Support session revoked by platform administrator.";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}

internal sealed record SupportSessionRevocationRejectedAuditEvent(
    Guid CompanyId,
    Guid SupportSessionId,
    Guid? ActorUserId,
    string Outcome,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "support.session-revoke-rejected";
    string IAuditEvent.EntityType => "SupportSession";
    Guid IAuditEvent.EntityId => SupportSessionId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Support session revocation attempt rejected ({Outcome}).";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => new { Outcome };
}
