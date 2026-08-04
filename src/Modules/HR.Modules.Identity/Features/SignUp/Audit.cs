using HR.SharedKernel;

namespace HR.Modules.Identity.Features.SignUp;

internal sealed record RegistrationCreatedAuditEvent(
    Guid CompanyId,
    Guid? AdminUserId,
    DateTimeOffset OccurredAt,
    bool Succeeded,
    string? FailureReason) : IAuditEvent
{
    string IAuditEvent.EventType => Succeeded ? "registration.created" : "registration.failed";
    string IAuditEvent.EntityType => "Company";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => AdminUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => Succeeded
        ? "Self-service registration completed"
        : $"Self-service registration failed: {FailureReason}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
