namespace HR.SharedKernel;

public sealed record RequiredAssetRemovedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid RequiredAssetId,
    Guid AssetCategoryId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.required-asset.removed";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Required asset removed from position profile";
    object? IAuditEvent.Before => new { RequiredAssetId, AssetCategoryId };
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
