using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.VerifyEmail;

// Follows HrSettingsUpdatedAuditEvent's exact shape (see
// HR.Modules.Companies/Features/UpdateHrSettings/Audit.cs). Published once per user on the first
// (and only the first) successful verification click — a repeat click on an already-Active
// company does not re-publish this event, see VerifyEmailHandler.
internal sealed record EmailVerificationSucceededAuditEvent(
    Guid CompanyId,
    Guid UserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "email-verification.succeeded";
    string IAuditEvent.EntityType => "UserProfile";
    Guid IAuditEvent.EntityId => UserId;
    Guid? IAuditEvent.ActorUserId => UserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Email verification succeeded";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}

// Published only on the actual first activation (never on an idempotent repeat verify-email
// click) — see VerifyEmailHandler.
internal sealed record CompanyActivatedAuditEvent(
    Guid CompanyId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "company.activated";
    string IAuditEvent.EntityType => "Company";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Company activated";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
