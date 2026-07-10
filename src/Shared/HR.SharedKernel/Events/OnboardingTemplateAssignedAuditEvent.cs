namespace HR.SharedKernel;

public sealed record OnboardingTemplateAssignedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid PositionProfileOnboardingTemplateId,
    Guid OnboardingTemplateId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.onboarding-template.assigned";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Onboarding template assigned to position profile";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { PositionProfileOnboardingTemplateId, OnboardingTemplateId };
    object? IAuditEvent.Metadata => null;
}
