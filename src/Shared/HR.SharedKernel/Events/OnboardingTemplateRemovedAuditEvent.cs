namespace HR.SharedKernel;

public sealed record OnboardingTemplateRemovedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid PositionProfileOnboardingTemplateId,
    Guid OnboardingTemplateId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.onboarding-template.removed";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Onboarding template removed from position profile";
    object? IAuditEvent.Before => new { PositionProfileOnboardingTemplateId, OnboardingTemplateId };
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
