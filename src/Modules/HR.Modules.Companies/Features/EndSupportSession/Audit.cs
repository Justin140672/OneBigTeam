using HR.SharedKernel;

namespace HR.Modules.Companies.Features.EndSupportSession;

internal sealed record SupportSessionEndedAuditEvent(
    Guid CompanyId,
    Guid SupportSessionId,
    Guid? ActorUserId,
    string Outcome,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => Outcome == "revoked" ? "support.session-revoked" : "support.session-revoke-rejected";
    string IAuditEvent.EntityType => "SupportSession";
    Guid IAuditEvent.EntityId => SupportSessionId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => Outcome == "revoked"
        ? "Support session ended and revoked by the support operator."
        : $"Support session end attempt rejected ({Outcome}).";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => new { Outcome, Source = "end-session" };
}
